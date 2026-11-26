#include "DMButler.h"

#include "Components/CapsuleComponent.h"
#include "Components/SpotLightComponent.h"
#include "Components/StaticMeshComponent.h"
#include "DMCharacter.h"
#include "DMNoiseSubsystem.h"
#include "DreamMovers.h"
#include "EngineUtils.h"
#include "NavigationSystem.h"
#include "HAL/IConsoleManager.h"
#include "Net/UnrealNetwork.h"

static TAutoConsoleVariable<int32> CVarButlerTrace(TEXT("dm.ButlerTrace"), 0,
	TEXT("1: log the butler's state, sight, timers and move every 0.25 s (server)."));

#define LOCTEXT_NAMESPACE "DreamMoversButler"

namespace
{
	const FLinearColor Velvet(0.42f, 0.04f, 0.06f);
	const FLinearColor Brass(0.85f, 0.62f, 0.28f);
	const FLinearColor Wax(0.95f, 0.92f, 0.82f);
	const FLinearColor Shadow(0.07f, 0.06f, 0.06f);
	const FLinearColor CalmLight(1.f, 0.72f, 0.42f);
	const FLinearColor AlarmLight(1.f, 0.3f, 0.15f);
}

ADMButler::ADMButler()
{
	DisplayName = LOCTEXT("Name", "촛대 집사");
	// About 190 cm: a head taller than the dolls' whole body.
	GetCapsuleComponent()->InitCapsuleSize(30.f, 95.f);

	const FVector Thin(0.07f, 0.07f, 0.95f);
	AddPart(TEXT("LegL"), CylinderShape, FVector(0.f, -12.f, -47.f), Thin, Shadow);
	AddPart(TEXT("LegR"), CylinderShape, FVector(0.f, 12.f, -47.f), Thin, Shadow);
	AddPart(TEXT("Coat"), CylinderShape, FVector(0.f, 0.f, 25.f), FVector(0.42f, 0.36f, 0.7f), Velvet, 0.85f);
	AddPart(TEXT("WaxL"), CylinderShape, FVector(10.f, -20.f, 5.f), FVector(0.12f, 0.12f, 0.35f), Wax, 0.4f);
	AddPart(TEXT("WaxR"), CylinderShape, FVector(10.f, 20.f, 5.f), FVector(0.12f, 0.12f, 0.35f), Wax, 0.4f);
	AddPart(TEXT("ArmL"), CylinderShape, FVector(12.f, -30.f, 25.f), FVector(0.05f, 0.05f, 0.6f), Shadow, 0.6f, 0.f, FRotator(0.f, 0.f, -20.f));
	AddPart(TEXT("ArmR"), CylinderShape, FVector(12.f, 30.f, 25.f), FVector(0.05f, 0.05f, 0.6f), Shadow, 0.6f, 0.f, FRotator(0.f, 0.f, 20.f));
	AddPart(TEXT("BellL"), ConeShape, FVector(18.f, -40.f, -8.f), FVector(0.14f), Brass, 0.2f, 1.f);
	AddPart(TEXT("BellR"), ConeShape, FVector(18.f, 40.f, -8.f), FVector(0.14f), Brass, 0.2f, 1.f);
	AddPart(TEXT("Dish"), CylinderShape, FVector(0.f, 0.f, 64.f), FVector(0.7f, 0.7f, 0.03f), Brass, 0.2f, 1.f);
	AddPart(TEXT("Cup"), CylinderShape, FVector(0.f, 0.f, 78.f), FVector(0.18f, 0.18f, 0.25f), Brass, 0.2f, 1.f);
	AddPart(TEXT("Candle"), CylinderShape, FVector(0.f, 0.f, 100.f), FVector(0.1f, 0.1f, 0.25f), Wax, 0.5f);
	AddPart(TEXT("Flame"), SphereShape, FVector(0.f, 0.f, 118.f), FVector(0.07f, 0.07f, 0.13f), FLinearColor(1.f, 0.65f, 0.2f), 0.9f);

	Candlelight = CreateDefaultSubobject<USpotLightComponent>(TEXT("Candlelight"));
	Candlelight->SetupAttachment(GetCapsuleComponent());
	Candlelight->SetRelativeLocationAndRotation(FVector(12.f, 0.f, 112.f), FRotator(-14.f, 0.f, 0.f));
	Candlelight->SetIntensityUnits(ELightUnits::Candelas);
	Candlelight->SetIntensity(150.f);
	Candlelight->SetLightColor(CalmLight);
	Candlelight->SetCastShadows(true);

	PatrolPoints = { FVector::ZeroVector };
}

void ADMButler::GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const
{
	Super::GetLifetimeReplicatedProps(OutLifetimeProps);
	DOREPLIFETIME(ADMButler, Suspicion);
}

void ADMButler::BeginPlay()
{
	// The light is the sight: keep them in sync with the tuning values.
	Candlelight->SetAttenuationRadius(SightRange);
	Candlelight->SetOuterConeAngle(SightHalfAngle);
	Candlelight->SetInnerConeAngle(SightHalfAngle * 0.6f);

	Super::BeginPlay();

	if (HasAuthority())
	{
		for (const FVector& Point : PatrolPoints)
		{
			WorldPatrol.Add(GetActorTransform().TransformPosition(Point));
		}
		ResumePatrol();
	}
}

void ADMButler::OnMonsterStateChanged()
{
	const bool bAlarmed = GetMonsterState() == EDMMonsterState::Chase;
	Candlelight->SetLightColor(bAlarmed ? AlarmLight : CalmLight);
	Candlelight->SetIntensity(bAlarmed ? 300.f : 150.f);
}

bool ADMButler::CanSee(const ADMCharacter* Doll, float* OutDistance, FString* OutWhy) const
{
	if (!Doll || Doll->IsDowned() || Doll->IsHiding())
	{
		if (OutWhy) { *OutWhy = TEXT("no doll, down or in a wardrobe"); }
		return false; // a closed wardrobe blocks the candlelight
	}
	const FVector Eye = Candlelight->GetComponentLocation();
	const float MaxCos = FMath::Cos(FMath::DegreesToRadians(SightHalfAngle));
	// The eyes, and any part of the body that is not under something (a doll half under a desk shows the other half).
	TArray<FVector> Points;
	Doll->GetExposedPoints(Points);
	FString Last;
	for (const FVector& TargetPoint : Points)
	{
		const FVector ToTarget = TargetPoint - Eye;
		const float Distance = ToTarget.Size();
		if (Distance > SightRange)
		{
			Last = FString::Printf(TEXT("out of range %.0f"), Distance);
			continue;
		}
		const float Cos = FVector::DotProduct(ToTarget / FMath::Max(Distance, 1.f), Candlelight->GetForwardVector());
		if (Cos < MaxCos)
		{
			Last = FString::Printf(TEXT("outside the cone (%.0f deg off the light axis)"), FMath::RadiansToDegrees(FMath::Acos(Cos)));
			continue;
		}
		FHitResult Hit;
		FCollisionQueryParams Params(SCENE_QUERY_STAT(DMButlerSight), false, this);
		if (GetWorld()->LineTraceSingleByChannel(Hit, Eye, TargetPoint, ECC_Visibility, Params) && Hit.GetActor() != Doll)
		{
			Last = FString::Printf(TEXT("blocked by %s.%s at %s"), *GetNameSafe(Hit.GetActor()), *GetNameSafe(Hit.GetComponent()), *Hit.ImpactPoint.ToCompactString());
			continue; // behind a wall, furniture or a carried antique
		}
		if (OutDistance)
		{
			*OutDistance = Distance;
		}
		if (OutWhy)
		{
			*OutWhy = FString::Printf(TEXT("visible: candle %s -> %s (%d of %d points exposed)"), *Eye.ToCompactString(), *TargetPoint.ToCompactString(), Points.Num() - 1, 8);
		}
		return true;
	}
	if (OutWhy)
	{
		*OutWhy = FString::Printf(TEXT("%s (%d of 8 body points exposed)"), *Last, Points.Num() - 1);
	}
	return false;
}

ADMCharacter* ADMButler::FindVisibleDoll(float& OutDistance) const
{
	ADMCharacter* Best = nullptr;
	OutDistance = TNumericLimits<float>::Max();
	const bool bIgnoring = GetWorld()->GetTimeSeconds() < IgnoreUnreachableUntil;
	for (TActorIterator<ADMCharacter> It(GetWorld()); It; ++It)
	{
		// Still at the spot it just gave up on: it already knows it cannot get there.
		if (bIgnoring && FVector::Dist2D(It->GetActorLocation(), UnreachableSpot) < 100.f)
		{
			continue;
		}
		float Distance;
		if (CanSee(*It, &Distance) && Distance < OutDistance)
		{
			Best = *It;
			OutDistance = Distance;
		}
	}
	return Best;
}

void ADMButler::RingBell()
{
	LastBellTime = GetWorld()->GetTimeSeconds();
	UDMNoiseSubsystem::Report(this, GetActorLocation(), BellRadius, this);
}

FVector ADMButler::ReachablePoint(const FVector& Location) const
{
	FNavLocation OnNav;
	const UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
	return Nav && Nav->ProjectPointToNavigation(Location, OnNav, FVector(200.f, 200.f, 250.f)) ? OnNav.Location : Location;
}

void ADMButler::BeginSearch(double Now, bool bUnreachable)
{
	if (bUnreachable)
	{
		UnreachableSpot = LastKnownLocation;
		IgnoreUnreachableUntil = Now + UnreachableIgnoreSeconds;
	}
	UE_LOG(LogDreamMovers, Log, TEXT("%s: %s, searching around %s"), *GetName(),
		bUnreachable ? TEXT("cannot reach the doll") : TEXT("lost sight of the doll"), *LastKnownLocation.ToCompactString());
	ChaseStallSince = 0.0;
	SetMonsterState(EDMMonsterState::Search);
	SpotProgress = 0.f;
	// The last place it saw the doll, not where the doll is now.
	MoveToLocation(ReachablePoint(LastKnownLocation), ChaseSpeed);
}

void ADMButler::ResumePatrol()
{
	SpotProgress = 0.f;
	Target.Reset();
	SetMonsterState(EDMMonsterState::Patrol);
	if (WorldPatrol.IsEmpty())
	{
		return;
	}
	// Continue from the nearest point rather than walking back across the house.
	float Best = TNumericLimits<float>::Max();
	for (int32 i = 0; i < WorldPatrol.Num(); ++i)
	{
		const float Dist = FVector::DistSquared(WorldPatrol[i], GetActorLocation());
		if (Dist < Best)
		{
			Best = Dist;
			PatrolIndex = i;
		}
	}
	PauseUntil = 0.0;
	MoveToLocation(WorldPatrol[PatrolIndex], PatrolSpeed);
}

void ADMButler::ServerThink(float DeltaSeconds)
{
	const double Now = GetWorld()->GetTimeSeconds();
	if (TryCatch())
	{
		ResumePatrol();
		return;
	}

	switch (GetMonsterState())
	{
	case EDMMonsterState::Patrol:
	case EDMMonsterState::Search:
	{
		float Distance;
		if (ADMCharacter* Seen = FindVisibleDoll(Distance))
		{
			// Up close it notices about twice as fast.
			SpotProgress += DeltaSeconds * (2.f - Distance / SightRange);
			if (SpotProgress >= SpotSeconds)
			{
				Target = Seen;
				LastKnownLocation = Seen->GetActorLocation();
				LastSeenTime = Now;
				SetMonsterState(EDMMonsterState::Chase);
				ChaseStallSince = 0.0;
				RingBell();
				MoveToLocation(ReachablePoint(Seen->GetActorLocation()), ChaseSpeed, true);
				break;
			}
		}
		else
		{
			SpotProgress = FMath::Max(0.f, SpotProgress - DeltaSeconds * 0.5f);
		}

		if (GetMonsterState() == EDMMonsterState::Search)
		{
			// Done looking around, or the goal turned out to be out of reach: never wait on it forever.
			if ((IsMoveDone() && StateAge() > SearchSeconds) || StateAge() > MaxSearchSeconds)
			{
				ResumePatrol();
			}
		}
		else if (WorldPatrol.Num() > 1 && IsMoveDone())
		{
			if (PauseUntil == 0.0)
			{
				PauseUntil = Now + PausePerPoint;
			}
			else if (Now >= PauseUntil)
			{
				PauseUntil = 0.0;
				PatrolIndex = (PatrolIndex + 1) % WorldPatrol.Num();
				UE_LOG(LogDreamMovers, Verbose, TEXT("%s: at %s, heading to patrol point %d"), *GetName(), *GetActorLocation().ToCompactString(), PatrolIndex);
				MoveToLocation(WorldPatrol[PatrolIndex], PatrolSpeed);
			}
		}
		break;
	}

	case EDMMonsterState::Chase:
	{
		ADMCharacter* Chased = Target.Get();
		if (!Chased || Chased->IsDowned())
		{
			ResumePatrol();
			break;
		}
		const bool bSees = CanSee(Chased);
		if (bSees)
		{
			LastSeenTime = Now;
			LastKnownLocation = Chased->GetActorLocation();
		}
		if (Now - LastSeenTime > LoseSightSeconds)
		{
			// Lost them: check where they were last seen, then give up.
			BeginSearch(Now, false);
			break;
		}
		// Standing still without being able to catch them: there is no way to them (under a desk, behind
		// something it cannot walk through). Waiting would last forever, so search instead.
		const bool bStalled = GetVelocity().Size2D() < 20.f && !CanCatch(Chased);
		if (GetVelocity().Size2D() < 20.f)
		{
			// Standing still in a chase it faces the doll (or where it last saw it), candle first.
			const FVector Toward = (bSees ? Chased->GetActorLocation() : LastKnownLocation) - GetActorLocation();
			SetActorRotation(FMath::RInterpConstantTo(GetActorRotation(), FRotator(0.f, Toward.Rotation().Yaw, 0.f), DeltaSeconds, 270.f));
		}
		ChaseStallSince = bStalled ? (ChaseStallSince == 0.0 ? Now : ChaseStallSince) : 0.0;
		if (bStalled && Now - ChaseStallSince > LoseSightSeconds)
		{
			BeginSearch(Now, true);
			break;
		}
		if (Now - LastRepathTime > 0.4)
		{
			LastRepathTime = Now;
			// Toward where it can actually stand: next to them if they are in view, else the last place it saw them.
			MoveToLocation(ReachablePoint(bSees ? Chased->GetActorLocation() : LastKnownLocation), ChaseSpeed, true);
		}
		if (Now - LastBellTime > BellInterval)
		{
			RingBell();
		}
		break;
	}

	default:
		ResumePatrol();
		break;
	}

	Suspicion = GetMonsterState() == EDMMonsterState::Chase ? 255 : uint8(FMath::Clamp(SpotProgress / SpotSeconds, 0.f, 1.f) * 255.f);
	Trace(Now);
}

void ADMButler::Trace(double Now)
{
	if (CVarButlerTrace.GetValueOnGameThread() == 0 || Now - LastTraceTime < 0.25)
	{
		return;
	}
	LastTraceTime = Now;
	static const TCHAR* States[] = { TEXT("Idle"), TEXT("Patrol"), TEXT("Chase"), TEXT("Charge"), TEXT("Search"), TEXT("Return") };
	const int32 StateIndex = int32(GetMonsterState());
	// The chased doll, or else the nearest one, so the sight result is about the doll that matters.
	const ADMCharacter* Doll = Target.Get();
	for (TActorIterator<ADMCharacter> It(GetWorld()); It && !Doll; ++It)
	{
		Doll = *It;
	}
	FString Why;
	const bool bSees = CanSee(Doll, nullptr, &Why);
	UE_LOG(LogDreamMovers, Display, TEXT("[ButlerTrace] t=%.2f state=%s age=%.2f suspicion=%d spot=%.2f sees=%d (%s) since-seen=%.2f last-known=%s doll=%s %s pos=%s speed=%.0f"),
		Now, StateIndex >= 0 && StateIndex < 6 ? States[StateIndex] : TEXT("?"), StateAge(), int32(Suspicion), SpotProgress, bSees ? 1 : 0, *Why,
		LastSeenTime > 0.0 ? Now - LastSeenTime : -1.0, *LastKnownLocation.ToCompactString(), Doll ? *Doll->GetActorLocation().ToCompactString() : TEXT("none"),
		*DescribeMove(), *GetActorLocation().ToCompactString(), GetVelocity().Size2D());
}

#undef LOCTEXT_NAMESPACE
