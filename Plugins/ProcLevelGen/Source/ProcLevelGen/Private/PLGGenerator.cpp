#include "PLGGenerator.h"

#include "Components/InstancedStaticMeshComponent.h"
#include "Camera/CameraActor.h"
#include "Camera/CameraComponent.h"
#include "Components/TextRenderComponent.h"
#include "Engine/CollisionProfile.h"
#include "Engine/StaticMesh.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Misc/CommandLine.h"
#include "NavigationPath.h"
#include "NavigationSystem.h"
#include "NavMesh/RecastNavMesh.h"
#include "Materials/MaterialInstanceDynamic.h"
#include "Materials/MaterialInterface.h"
#include "Net/UnrealNetwork.h"
#include "PLGLayout.h"
#include "TimerManager.h"
#include "UnrealClient.h"
#include "ProcLevelGen.h"
#include "UObject/ConstructorHelpers.h"

namespace
{
	/** Marks everything the generator builds, so a rebuild can sweep it up even after PIE duplication. */
	const FName GeneratedTag(TEXT("PLGGenerated"));
}

APLGGenerator::APLGGenerator()
{
	PrimaryActorTick.bCanEverTick = false;
	bReplicates = true;
	bAlwaysRelevant = true;

	SetRootComponent(CreateDefaultSubobject<USceneComponent>(TEXT("Root")));

	static ConstructorHelpers::FObjectFinder<UStaticMesh> Cube(TEXT("/Engine/BasicShapes/Cube.Cube"));
	static ConstructorHelpers::FObjectFinder<UMaterialInterface> Shape(TEXT("/Engine/BasicShapes/BasicShapeMaterial.BasicShapeMaterial"));
	CubeMesh = Cube.Object;
	ShapeMaterial = Shape.Object;

	Modules = PLG::MakeSampleModules();
	Themes = PLG::MakeSampleThemes();
}

void APLGGenerator::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(APLGGenerator, Seed);
}

void APLGGenerator::BeginPlay()
{
	Super::BeginPlay();
	if (HasAuthority())
	{
		if (bNewSeedEachRun || Seed == 0)
		{
			Seed = FMath::RandRange(1, MAX_int32 - 1);
		}
#if !UE_BUILD_SHIPPING
		// Test hooks: -plgseed=N fixes the seed, -plgtestkit=empty|impossible makes generation fail on purpose.
		int32 ForcedSeed = 0;
		if (FParse::Value(FCommandLine::Get(), TEXT("plgseed="), ForcedSeed) && ForcedSeed != 0)
		{
			Seed = ForcedSeed;
		}
		FString TestKit;
		if (FParse::Value(FCommandLine::Get(), TEXT("plgtestkit="), TestKit))
		{
			if (TestKit == TEXT("empty"))
			{
				Modules.Reset();
			}
			else if (TestKit == TEXT("impossible"))
			{
				// The start and the hub alone need more room than this.
				MaxHalfExtent = 1000.f;
			}
			UE_LOG(LogProcLevelGen, Warning, TEXT("%s: test kit '%s'"), *GetName(), *TestKit);
		}
#endif
		Generate(true);
#if !UE_BUILD_SHIPPING
		if (FParse::Param(FCommandLine::Get(), TEXT("plgselftest")))
		{
			FTimerHandle Handle;
			GetWorldTimerManager().SetTimer(Handle, this, &APLGGenerator::RunSelfTestChecks, 8.f, false);
		}
#endif
	}
	else if (!bNewSeedEachRun)
	{
		// The saved seed is the server's seed too, so OnRep may never fire.
		Generate(false);
	}
}

void APLGGenerator::OnRep_Seed()
{
	// Clients build exactly the seed the server settled on.
	Generate(false);
}

void APLGGenerator::Preview()
{
	if (Seed == 0)
	{
		Seed = 1;
	}
	Generate(false);
}

void APLGGenerator::PreviewNewSeed()
{
	Seed = FMath::RandRange(1, MAX_int32 - 1);
	Generate(false);
}

void APLGGenerator::ClearPreview()
{
	Layout = FPLGLayout();
	BuildGraybox();
}

FTransform APLGGenerator::GetModuleTransform(int32 PlacedIndex) const
{
	if (!Layout.Modules.IsValidIndex(PlacedIndex))
	{
		return GetActorTransform();
	}
	const FPLGPlacedModule& Placed = Layout.Modules[PlacedIndex];
	const FTransform Local(FRotator(0.f, 90.f * Placed.Quarter, 0.f), FVector(Placed.Location, 0.f));
	return Local * GetActorTransform();
}

TArray<FPLGSpawnSlot> APLGGenerator::GetSpawnSlots(FName Tag) const
{
	TArray<FPLGSpawnSlot> Slots;
	for (int32 i = 0; i < Layout.Modules.Num(); ++i)
	{
		const FPLGPlacedModule& Placed = Layout.Modules[i];
		if (Placed.bRemoved || !Modules.IsValidIndex(Placed.Def))
		{
			continue;
		}
		const FPLGModuleDef& Def = Modules[Placed.Def];
		const FTransform ModuleToWorld = GetModuleTransform(i);
		for (const FPLGSpawnPoint& Point : Def.SpawnPoints)
		{
			if (Point.Tag == Tag)
			{
				FPLGSpawnSlot& Slot = Slots.AddDefaulted_GetRef();
				Slot.Transform = FTransform(FRotator(0.f, Point.Yaw, 0.f), Point.Location) * ModuleToWorld;
				Slot.Module = i;
				Slot.Role = Def.Role;
				Slot.Theme = Placed.Theme;
			}
		}
	}
	return Slots;
}

FRandomStream APLGGenerator::MakeContentStream() const
{
	return FRandomStream(int32(HashCombineFast(uint32(Seed), 0x5EEDC0DEu)));
}

uint32 APLGGenerator::GetLayoutHash() const
{
	return PLG::HashLayout(Layout);
}

void APLGGenerator::Generate(bool bAllowReseed)
{
	for (const FString& Problem : PLG::ValidateModules(Modules, GridSize))
	{
		UE_LOG(LogProcLevelGen, Warning, TEXT("%s: kit: %s"), *GetName(), *Problem);
	}

	FPLGLayoutSettings Settings;
	Settings.Modules = Modules;
	Settings.NumThemes = Themes.Num();
	Settings.RoomCount = RoomCount;
	Settings.CorridorChance = CorridorChance;
	Settings.MaxHalfExtent = MaxHalfExtent;

	bGenerationSucceeded = false;
	const int32 FirstSeed = Seed;
	const int32 Tries = 1 + (bAllowReseed ? MaxSeedRetries : 0);
	for (int32 Try = 0; Try < Tries && !bGenerationSucceeded; ++Try)
	{
		const int32 TrySeed = Try == 0 ? FirstSeed : FMath::Max(1, int32(HashCombineFast(uint32(FirstSeed), uint32(0xA11CE + Try)) & 0x7FFFFFFF));
		FString Report;
		if (!PLG::GenerateLayout(Settings, TrySeed, Layout, Report))
		{
			UE_LOG(LogProcLevelGen, Error, TEXT("%s: %s"), *GetName(), *Report);
			continue;
		}
		// Check the result on its own before trusting it, also when a fallback seed made it.
		const TArray<FString> Problems = PLG::ValidateLayout(Modules, Layout, RoomCount);
		if (Problems.Num() > 0)
		{
			UE_LOG(LogProcLevelGen, Error, TEXT("%s: seed %d gave an invalid layout: %s"), *GetName(), TrySeed, *FString::Join(Problems, TEXT("; ")));
			continue;
		}
		Seed = TrySeed;
		bGenerationSucceeded = true;
		UE_LOG(LogProcLevelGen, Log, TEXT("%s: %s, layout hash %08x%s"), *GetName(), *Report, GetLayoutHash(),
			Try > 0 ? *FString::Printf(TEXT(" (fallback %d after seed %d failed)"), Try, FirstSeed) : TEXT(""));
		for (int32 i = 0; i < Layout.Modules.Num(); ++i)
		{
			const FPLGPlacedModule& Placed = Layout.Modules[i];
			if (!Placed.bRemoved)
			{
				UE_LOG(LogProcLevelGen, Verbose, TEXT("  [%d] %s at (%.0f, %.0f) yaw %d depth %d theme %d"), i, *Modules[Placed.Def].Name.ToString(),
					Placed.Location.X, Placed.Location.Y, Placed.Quarter * 90, Placed.Depth, Placed.Theme);
			}
		}
	}
	if (!bGenerationSucceeded)
	{
		Layout = FPLGLayout();
		UE_LOG(LogProcLevelGen, Error, TEXT("%s: generation failed after %d seed(s), starting from seed %d; nothing was built"), *GetName(), Tries, FirstSeed);
	}

	BuildGraybox();
	if (bGenerationSucceeded && GetWorld()->IsGameWorld())
	{
		// The whole house just appeared: rebuild the navmesh over all of it rather than relying on per-instance updates.
		if (UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld()))
		{
			FBox2D Area(ForceInit);
			for (const FPLGPlacedModule& Placed : Layout.Modules)
			{
				if (!Placed.bRemoved)
				{
					Area += PLG::Footprint(Modules[Placed.Def], Placed);
				}
			}
			const FBox Local(FVector(Area.Min, -FloorThickness - 10.f), FVector(Area.Max, WallHeight + 10.f));
			Nav->AddDirtyArea(Local.TransformBy(GetActorTransform()), ENavigationDirtyFlag::All, TEXT("PLGLayoutBuilt"));
		}
	}
	OnGenerated.Broadcast(this, bGenerationSucceeded);
}

bool APLGGenerator::IsNavigationReady(FString& OutWhy) const
{
	UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
	if (!Nav || !Nav->GetDefaultNavDataInstance(FNavigationSystem::DontCreate))
	{
		OutWhy = TEXT("no navigation data");
		return false;
	}
	if (Nav->IsNavigationBuildInProgress())
	{
		OutWhy = TEXT("navmesh build in progress");
		return false;
	}
	for (int32 i = 0; i < Layout.Modules.Num(); ++i)
	{
		FVector OnNav;
		if (!Layout.Modules[i].bRemoved && !ProjectIntoModule(*Nav, i, OnNav))
		{
			const ARecastNavMesh* Recast = Cast<ARecastNavMesh>(Nav->GetDefaultNavDataInstance(FNavigationSystem::DontCreate));
			OutWhy = FString::Printf(TEXT("module %d has no navmesh near its pivot yet (%s, %d tiles)"), i, *GetNameSafe(Recast), Recast ? Recast->GetNavMeshTilesCount() : -1);
			return false;
		}
	}
	OutWhy.Reset();
	return true;
}

bool APLGGenerator::ProjectIntoModule(UNavigationSystemV1& Nav, int32 PlacedIndex, FVector& OutPoint) const
{
	// Within a quarter of the module's size of its pivot, so furniture on the pivot does not matter,
	// and inside its own footprint, so a neighbor's navmesh does not count.
	const FPLGPlacedModule& Placed = Layout.Modules[PlacedIndex];
	const FPLGModuleDef& Def = Modules[Placed.Def];
	const float Reach = FMath::Min(Def.Size.X, Def.Size.Y) * 0.25f;
	FNavLocation OnNav;
	if (!Nav.ProjectPointToNavigation(GetModuleTransform(PlacedIndex).GetLocation(), OnNav, FVector(Reach, Reach, 150.f)))
	{
		return false;
	}
	const FVector Local = GetActorTransform().InverseTransformPosition(OnNav.Location);
	if (!PLG::Footprint(Def, Placed).IsInside(FVector2D(Local.X, Local.Y)))
	{
		return false;
	}
	OutPoint = OnNav.Location;
	return true;
}

int32 APLGGenerator::RunGenerationChecks(TFunctionRef<void(bool, const FString&)> Report) const
{
	int32 Failures = 0;
	auto Check = [&](bool bPassed, const FString& What)
	{
		Failures += bPassed ? 0 : 1;
		Report(bPassed, What);
	};

	int32 Live = 0;
	for (const FPLGPlacedModule& Placed : Layout.Modules)
	{
		Live += Placed.bRemoved ? 0 : 1;
	}
	Check(bGenerationSucceeded && Live > 0, FString::Printf(TEXT("generation succeeded: seed %d, %d modules, layout hash %08x"), Seed, Live, GetLayoutHash()));
	if (!bGenerationSucceeded)
	{
		return Failures;
	}
	const TArray<FString> Problems = PLG::ValidateLayout(Modules, Layout, RoomCount);
	Check(Problems.Num() == 0, Problems.Num() == 0 ? FString(TEXT("layout passes ValidateLayout")) : FString::Printf(TEXT("layout invalid: %s"), *FString::Join(Problems, TEXT("; "))));

	int32 Instances = 0;
	for (const UInstancedStaticMeshComponent* Component : Boxes)
	{
		Instances += Component ? Component->GetInstanceCount() : 0;
	}
	Check(Instances > 0, FString::Printf(TEXT("%d graybox boxes built"), Instances));

	// The player spawned in the start module and stands on generated floor, not falling through.
	const APlayerController* PC = GetWorld()->GetFirstPlayerController();
	const APawn* Pawn = PC ? PC->GetPawn() : nullptr;
	if (Pawn)
	{
		FHitResult Hit;
		FCollisionQueryParams Params(SCENE_QUERY_STAT(PLGSelfTest), false, Pawn);
		const FVector From = Pawn->GetActorLocation();
		const bool bFloor = GetWorld()->LineTraceSingleByChannel(Hit, From, From - FVector(0.f, 0.f, 300.f), ECC_Visibility, Params) && Hit.GetActor() == this;
		Check(bFloor && FMath::Abs(Pawn->GetVelocity().Z) < 10.f,
			FString::Printf(TEXT("player stands on the generated floor (z %.0f, vz %.0f)"), From.Z, Pawn->GetVelocity().Z));
	}
	else
	{
		Check(false, TEXT("player has a pawn"));
	}

	// Navigation from the start module reaches every module. A module with no navmesh near its pivot is a failure, not skipped.
	UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
	if (!Nav)
	{
		Check(false, TEXT("navigation system exists"));
		return Failures;
	}
	FVector StartOnNav;
	if (!ProjectIntoModule(*Nav, 0, StartOnNav))
	{
		Check(false, TEXT("start module has navmesh near its pivot"));
		return Failures;
	}
	int32 Reached = 0;
	FString Missed;
	for (int32 i = 1; i < Layout.Modules.Num(); ++i)
	{
		if (Layout.Modules[i].bRemoved)
		{
			continue;
		}
		const FString Name = FString::Printf(TEXT("%s[%d]"), *Modules[Layout.Modules[i].Def].Name.ToString(), i);
		FVector GoalOnNav;
		if (!ProjectIntoModule(*Nav, i, GoalOnNav))
		{
			Missed += TEXT(" ") + Name + TEXT("(no navmesh near its pivot)");
			continue;
		}
		const UNavigationPath* Path = Nav->FindPathToLocationSynchronously(GetWorld(), StartOnNav, GoalOnNav);
		if (Path && Path->IsValid() && !Path->IsPartial())
		{
			++Reached;
		}
		else
		{
			Missed += TEXT(" ") + Name + (Path && Path->IsValid() ? TEXT("(partial path)") : TEXT("(no path)"));
		}
	}
	Check(Missed.IsEmpty(), FString::Printf(TEXT("navigation reaches %d/%d modules from the start%s"), Reached, Live - 1,
		Missed.IsEmpty() ? TEXT("") : *(TEXT(", missed:") + Missed)));
	return Failures;
}

UInstancedStaticMeshComponent* APLGGenerator::GetBoxes(const FLinearColor& Color)
{
	for (int32 i = 0; i < BoxColors.Num(); ++i)
	{
		if (BoxColors[i].Equals(Color) && Boxes[i])
		{
			return Boxes[i];
		}
	}
	UInstancedStaticMeshComponent* Instances = NewObject<UInstancedStaticMeshComponent>(this, NAME_None, RF_Transient);
	Instances->ComponentTags.Add(GeneratedTag);
	Instances->SetMobility(EComponentMobility::Movable);
	Instances->SetStaticMesh(CubeMesh);
	Instances->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
	if (UMaterialInstanceDynamic* Material = UMaterialInstanceDynamic::Create(ShapeMaterial, Instances))
	{
		Material->SetVectorParameterValue(TEXT("Color"), Color);
		Instances->SetMaterial(0, Material);
	}
	Instances->SetupAttachment(GetRootComponent());
	Instances->RegisterComponent();
	BoxColors.Add(Color);
	Boxes.Add(Instances);
	return Instances;
}

void APLGGenerator::BuildGraybox()
{
	TInlineComponentArray<UActorComponent*> Existing(this);
	for (UActorComponent* Component : Existing)
	{
		if (Component && Component->ComponentHasTag(GeneratedTag))
		{
			Component->DestroyComponent();
		}
	}
	BoxColors.Reset();
	Boxes.Reset();
	Labels.Reset();

	for (int32 i = 0; i < Layout.Modules.Num(); ++i)
	{
		const FPLGPlacedModule& Placed = Layout.Modules[i];
		if (Placed.bRemoved || !Modules.IsValidIndex(Placed.Def))
		{
			continue;
		}
		const FTransform ModuleToWorld = GetModuleTransform(i);
		AddModuleGeometry(Placed, ModuleToWorld);

		// Labels are for the editor's top view only; in a game they would just be extra primitives on the floor.
		if (bShowLabels && !GetWorld()->IsGameWorld())
		{
			FString Text = Modules[Placed.Def].Name.ToString();
			if (Themes.IsValidIndex(Placed.Theme))
			{
				Text += TEXT("\n") + Themes[Placed.Theme].Name.ToString();
			}
			UTextRenderComponent* Label = NewObject<UTextRenderComponent>(this, NAME_None, RF_Transient);
			Label->ComponentTags.Add(GeneratedTag);
			Label->SetMobility(EComponentMobility::Movable);
			Label->SetText(FText::FromString(Text));
			Label->SetHorizontalAlignment(EHTA_Center);
			Label->SetVerticalAlignment(EVRTA_TextCenter);
			Label->SetWorldSize(80.f);
			Label->SetTextRenderColor(FColor::Black);
			// Readable from the editor's top view; players never see it.
			Label->SetHiddenInGame(true);
			// A label must not cut a hole in the navmesh at every module pivot.
			Label->SetCollisionEnabled(ECollisionEnabled::NoCollision);
			Label->SetCanEverAffectNavigation(false);
			Label->SetupAttachment(GetRootComponent());
			Label->SetWorldLocationAndRotation(ModuleToWorld.TransformPosition(FVector(0.f, 0.f, 10.f)), FRotator(90.f, ModuleToWorld.Rotator().Yaw, 0.f));
			Label->RegisterComponent();
			Labels.Add(Label);
		}
	}
}

void APLGGenerator::AddModuleGeometry(const FPLGPlacedModule& Placed, const FTransform& ModuleToWorld)
{
	const FPLGModuleDef& Def = Modules[Placed.Def];

	FLinearColor FloorColor = FixedFloorColor;
	FLinearColor WallColor = DefaultWallColor;
	if (Def.Role == EPLGRole::Corridor)
	{
		FloorColor = CorridorFloorColor;
	}
	else if (Def.Role == EPLGRole::Room && Themes.IsValidIndex(Placed.Theme))
	{
		FloorColor = Themes[Placed.Theme].FloorColor;
		WallColor = Themes[Placed.Theme].WallColor;
	}
	UInstancedStaticMeshComponent* Floor = GetBoxes(FloorColor);
	UInstancedStaticMeshComponent* Walls = GetBoxes(WallColor);

	// Engine cube: 100 cm, centered. Boxes are given in module space (pivot = floor top, footprint center).
	auto AddBox = [&ModuleToWorld](UInstancedStaticMeshComponent* Instances, const FVector& Center, const FVector& Size)
	{
		Instances->AddInstance(FTransform(FQuat::Identity, Center, Size / 100.f) * ModuleToWorld, /*bWorldSpace*/ true);
	};

	AddBox(Floor, FVector(0.f, 0.f, -FloorThickness * 0.5f), FVector(Def.Size.X, Def.Size.Y, FloorThickness));
	if (Def.Props.Num() > 0)
	{
		UInstancedStaticMeshComponent* Props = GetBoxes(PropColor);
		for (const FPLGPropBox& Prop : Def.Props)
		{
			AddBox(Props, Prop.Center, Prop.Size);
		}
	}

	// Half the wall thickness, inside the footprint: the neighbor builds the other half.
	const float Half = WallThickness * 0.5f;
	for (int32 EdgeIndex = 0; EdgeIndex < 4; ++EdgeIndex)
	{
		const EPLGEdge Edge = EPLGEdge(EdgeIndex);
		const bool bXEdge = Edge == EPLGEdge::PosX || Edge == EPLGEdge::NegX;
		const float Sign = (Edge == EPLGEdge::PosX || Edge == EPLGEdge::PosY) ? 1.f : -1.f;
		const float Length = bXEdge ? Def.Size.Y : Def.Size.X;
		const float Across = Sign * ((bXEdge ? Def.Size.X : Def.Size.Y) * 0.5f - Half * 0.5f);
		// Walls on the Y edges stop where the X edge walls already fill the corners.
		const float Trim = bXEdge ? 0.f : Half;

		TArray<FVector2D> Gaps;
		for (int32 S = 0; S < Def.Sockets.Num(); ++S)
		{
			const FPLGSocket& Socket = Def.Sockets[S];
			if (Socket.Edge == Edge && Placed.Links.IsValidIndex(S) && Placed.Links[S] != INDEX_NONE)
			{
				Gaps.Emplace(Socket.Offset - Socket.Width * 0.5f, Socket.Offset + Socket.Width * 0.5f);
			}
		}
		Gaps.Sort([](const FVector2D& A, const FVector2D& B) { return A.X < B.X; });

		auto Segment = [&](float From, float To, float Bottom, float Top)
		{
			if (To - From < 0.5f || Top - Bottom < 0.5f)
			{
				return;
			}
			const float Along = (From + To) * 0.5f;
			const float Z = (Bottom + Top) * 0.5f;
			const FVector Center = bXEdge ? FVector(Across, Along, Z) : FVector(Along, Across, Z);
			const FVector Size = bXEdge ? FVector(Half, To - From, Top - Bottom) : FVector(To - From, Half, Top - Bottom);
			AddBox(Walls, Center, Size);
		};

		float Cursor = -Length * 0.5f + Trim;
		for (const FVector2D& Gap : Gaps)
		{
			Segment(Cursor, Gap.X, 0.f, WallHeight);
			Segment(Gap.X, Gap.Y, DoorHeight, WallHeight); // lintel
			Cursor = Gap.Y;
		}
		Segment(Cursor, Length * 0.5f - Trim, 0.f, WallHeight);
	}
}

#if !UE_BUILD_SHIPPING
void APLGGenerator::SelfTestCheck(bool bPassed, const FString& What)
{
	SelfTestFailures += bPassed ? 0 : 1;
	UE_LOG(LogProcLevelGen, Display, TEXT("[PLGSelfTest] %s %s"), bPassed ? TEXT("PASS") : TEXT("FAIL"), *What);
}

void APLGGenerator::RunSelfTestChecks()
{
	RunGenerationChecks([this](bool bPassed, const FString& What) { SelfTestCheck(bPassed, What); });
	FScreenshotRequest::RequestScreenshot(TEXT("PLGSelfTest_Player"), false, false);
	FTimerHandle Handle;
	GetWorldTimerManager().SetTimer(Handle, this, &APLGGenerator::TakeTopDownShot, 1.f, false);
}

void APLGGenerator::TakeTopDownShot()
{
	FBox2D Bounds(ForceInit);
	for (const FPLGPlacedModule& Placed : Layout.Modules)
	{
		if (!Placed.bRemoved)
		{
			Bounds += PLG::Footprint(Modules[Placed.Def], Placed);
		}
	}
	APlayerController* PC = GetWorld()->GetFirstPlayerController();
	if (PC && Bounds.bIsValid)
	{
		PC->ConsoleCommand(TEXT("show Navigation"));
		// Looking straight down with yaw 0, +X is up on screen and +Y is right. 90 degree horizontal FOV on 16:9.
		const FVector2D Center = Bounds.GetCenter();
		const float Height = FMath::Max(Bounds.GetSize().Y, Bounds.GetSize().X * 16.f / 9.f) * 0.55f + 600.f;
		const FVector Location = GetActorTransform().TransformPosition(FVector(Center, Height));
		if (ACameraActor* Camera = GetWorld()->SpawnActor<ACameraActor>(Location, FRotator(-90.f, 0.f, 0.f)))
		{
			Camera->GetCameraComponent()->SetFieldOfView(90.f);
			Camera->GetCameraComponent()->bConstrainAspectRatio = false;
			PC->SetViewTarget(Camera);
		}
	}
	FTimerHandle ShotHandle;
	GetWorldTimerManager().SetTimer(ShotHandle, FTimerDelegate::CreateWeakLambda(this, []()
	{
		FScreenshotRequest::RequestScreenshot(TEXT("PLGSelfTest_Top"), false, false);
	}), 1.f, false);
	FTimerHandle QuitHandle;
	GetWorldTimerManager().SetTimer(QuitHandle, FTimerDelegate::CreateWeakLambda(this, [this]()
	{
		UE_LOG(LogProcLevelGen, Display, TEXT("[PLGSelfTest] %s, %d failure(s)"), SelfTestFailures == 0 ? TEXT("ALL PASSED") : TEXT("FAILED"), SelfTestFailures);
		// Non-zero exit code on failure. Forced: a normal exit request ignores the code in the editor executable.
		FPlatformMisc::RequestExitWithStatus(true, SelfTestFailures == 0 ? 0 : 1);
	}), 3.f, false);
}
#endif
