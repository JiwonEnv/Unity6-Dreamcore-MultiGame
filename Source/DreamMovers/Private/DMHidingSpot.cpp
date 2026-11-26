#include "DMHidingSpot.h"

#include "Components/StaticMeshComponent.h"
#include "Components/CapsuleComponent.h"
#include "DMCharacter.h"
#include "DreamMovers.h"
#include "DMNoiseSubsystem.h"
#include "Engine/CollisionProfile.h"
#include "Engine/StaticMesh.h"
#include "EngineUtils.h"
#include "Materials/MaterialInterface.h"
#include "Net/UnrealNetwork.h"
#include "UObject/ConstructorHelpers.h"

ADMHidingSpot::ADMHidingSpot()
{
	bReplicates = true;

	SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));

	static ConstructorHelpers::FObjectFinder<UStaticMesh> CubeMesh(TEXT("/Engine/BasicShapes/Cube.Cube"));
	static ConstructorHelpers::FObjectFinder<UMaterialInterface> ShapeMaterial(TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"));
	Body = CreateDefaultSubobject<UStaticMeshComponent>(TEXT("Body"));
	Body->SetupAttachment(GetRootComponent());
	Body->SetStaticMesh(CubeMesh.Object);
	Body->SetMaterial(0, ShapeMaterial.Object);
	Body->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
}

void ADMHidingSpot::OnConstruction(const FTransform& Transform)
{
	Super::OnConstruction(Transform);
	ApplyShape();
}

void ADMHidingSpot::Configure(const FVector& InSize, int32 InCapacity)
{
	Size = InSize;
	Capacity = FMath::Clamp(InCapacity, 1, 2);
	ApplyShape();
}

void ADMHidingSpot::OnRep_Shape()
{
	ApplyShape();
}

void ADMHidingSpot::ApplyShape()
{
	// The engine cube is 100 cm and centered.
	Body->SetRelativeLocation(FVector(0.f, 0.f, Size.Z * 0.5f));
	Body->SetRelativeScale3D(Size / 100.f);
}

void ADMHidingSpot::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(ADMHidingSpot, NumOccupants);
	DOREPLIFETIME_CONDITION(ADMHidingSpot, Size, COND_InitialOnly);
	DOREPLIFETIME_CONDITION(ADMHidingSpot, Capacity, COND_InitialOnly);
}

ADMHidingSpot* ADMHidingSpot::FindReachable(const ADMCharacter* Doll)
{
	if (!Doll)
	{
		return nullptr;
	}
	ADMHidingSpot* Best = nullptr;
	float BestDistance = TNumericLimits<float>::Max();
	for (TActorIterator<ADMHidingSpot> It(Doll->GetWorld()); It; ++It)
	{
		// Distance to the front face, measured in the wardrobe's own space.
		const FVector Local = It->GetActorTransform().InverseTransformPosition(Doll->GetActorLocation());
		const float Front = Local.X - It->Size.X * 0.5f;
		const bool bInFront = Front > -10.f && Front < It->ReachDistance && FMath::Abs(Local.Y) < It->Size.Y * 0.5f + 30.f;
		// Standing (or crouching) on the wardrobe's own floor, not on something above or below it.
		const bool bSameFloor = Local.Z > 0.f && Local.Z < It->Size.Z;
		if (!bInFront || !bSameFloor || Front >= BestDistance)
		{
			continue;
		}
		// Nothing between the doll and the doors.
		const FVector Doors = It->GetActorTransform().TransformPosition(FVector(It->Size.X * 0.5f + 1.f, FMath::Clamp(Local.Y, -It->Size.Y * 0.5f, It->Size.Y * 0.5f), Local.Z));
		FHitResult Hit;
		FCollisionQueryParams Params(SCENE_QUERY_STAT(DMHidingReach), false, Doll);
		if (Doll->GetWorld()->LineTraceSingleByChannel(Hit, Doll->GetActorLocation(), Doors, ECC_Visibility, Params) && Hit.GetActor() != *It)
		{
			continue;
		}
		Best = *It;
		BestDistance = Front;
	}
	return Best;
}

void ADMHidingSpot::PurgeOccupants()
{
	Occupants.RemoveAll([](const TWeakObjectPtr<ADMCharacter>& Doll) { return !Doll.IsValid() || Doll->IsDowned(); });
	NumOccupants = Occupants.Num();
}

FVector ADMHidingSpot::GetSlotLocation(int32 Slot) const
{
	const float SideOffset = Capacity > 1 ? (Slot == 0 ? -0.25f : 0.25f) * Size.Y : 0.f;
	return GetActorTransform().TransformPosition(FVector(0.f, SideOffset, 60.f));
}

FVector ADMHidingSpot::GetExitLocation(int32 Slot) const
{
	const float SideOffset = Capacity > 1 ? (Slot == 0 ? -0.25f : 0.25f) * Size.Y : 0.f;
	return GetActorTransform().TransformPosition(FVector(Size.X * 0.5f + 50.f, SideOffset, 60.f));
}

bool ADMHidingSpot::TryEnter(ADMCharacter* Doll)
{
	if (!HasAuthority() || !Doll)
	{
		return false;
	}
	PurgeOccupants();
	if (!HasRoom() || Occupants.Contains(Doll))
	{
		return false;
	}
	// Take the free side so two dolls do not end up on top of each other.
	int32 Slot = 0;
	if (Capacity > 1 && Occupants.Num() == 1)
	{
		const FVector Local = GetActorTransform().InverseTransformPosition(Occupants[0]->GetActorLocation());
		Slot = Local.Y < 0.f ? 1 : 0;
	}
	Occupants.Add(Doll);
	NumOccupants = Occupants.Num();
	UDMNoiseSubsystem::Report(this, GetActorLocation(), DoorNoiseRadius, this);
	// Facing out through the doors, the way you would peek through the gap.
	Doll->EnterHiding(this, GetSlotLocation(Slot), GetActorRotation());
	return true;
}

bool ADMHidingSpot::IsExitClear(const ADMCharacter* Doll, const FVector& Location) const
{
	const UCapsuleComponent* Capsule = Doll->GetCapsuleComponent();
	FCollisionQueryParams Params(SCENE_QUERY_STAT(DMHidingExit), false, Doll);
	const FCollisionShape Shape = FCollisionShape::MakeCapsule(Capsule->GetUnscaledCapsuleRadius(), Capsule->GetUnscaledCapsuleHalfHeight());
	if (GetWorld()->OverlapBlockingTestByChannel(Location, FQuat::Identity, ECC_Pawn, Shape, Params))
	{
		return false;
	}
	FHitResult Floor;
	return GetWorld()->LineTraceSingleByChannel(Floor, Location, Location - FVector(0.f, 0.f, Capsule->GetUnscaledCapsuleHalfHeight() + 40.f), ECC_Visibility, Params);
}

bool ADMHidingSpot::Leave(ADMCharacter* Doll)
{
	if (!HasAuthority() || !Doll)
	{
		return false;
	}
	const int32 Index = Occupants.IndexOfByKey(Doll);
	if (Index == INDEX_NONE)
	{
		return false;
	}
	const FVector Local = GetActorTransform().InverseTransformPosition(Doll->GetActorLocation());
	const int32 Slot = Capacity > 1 && Local.Y > 0.f ? 1 : 0;
	// Own side first, then the other side, then straight out and a step further.
	TArray<FVector> Exits = { GetExitLocation(Slot) };
	if (Capacity > 1)
	{
		Exits.Add(GetExitLocation(1 - Slot));
	}
	Exits.Add(GetActorTransform().TransformPosition(FVector(Size.X * 0.5f + 50.f, 0.f, 60.f)));
	Exits.Add(GetActorTransform().TransformPosition(FVector(Size.X * 0.5f + 100.f, 0.f, 60.f)));
	const FVector* Exit = Exits.FindByPredicate([this, Doll](const FVector& Location) { return IsExitClear(Doll, Location); });
	if (!Exit)
	{
		// Leave state untouched: still inside, still hidden, still taking up the place.
		UE_LOG(LogDreamMovers, Log, TEXT("%s: every exit of %s is blocked; staying inside"), *Doll->GetName(), *GetName());
		return false;
	}
	Occupants.RemoveAt(Index);
	NumOccupants = Occupants.Num();
	UDMNoiseSubsystem::Report(this, GetActorLocation(), DoorNoiseRadius, this);
	Doll->LeaveHiding(*Exit);
	return true;
}

void ADMHidingSpot::Release(ADMCharacter* Doll)
{
	if (HasAuthority() && Occupants.Remove(Doll) > 0)
	{
		NumOccupants = Occupants.Num();
	}
}
