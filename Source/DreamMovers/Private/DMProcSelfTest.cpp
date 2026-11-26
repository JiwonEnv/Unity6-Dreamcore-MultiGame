#include "DMProcSelfTest.h"

#include "AIController.h"
#include "Components/CapsuleComponent.h"
#include "Components/StaticMeshComponent.h"
#include "DMBellHorse.h"
#include "DMButler.h"
#include "DMCharacter.h"
#include "DMCottonBall.h"
#include "DMExtractionZone.h"
#include "DMGameMode.h"
#include "DMGameState.h"
#include "DMGrabComponent.h"
#include "DMHidingSpot.h"
#include "DMHUD.h"
#include "DMMonster.h"
#include "DMNoiseSubsystem.h"
#include "DMValuable.h"
#include "DreamMovers.h"
#include "Engine/CollisionProfile.h"
#include "Engine/StaticMesh.h"
#include "Engine/StaticMeshActor.h"
#include "EngineUtils.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/PlayerController.h"
#include "HAL/IConsoleManager.h"
#include "Misc/CommandLine.h"
#include "NavigationPath.h"
#include "NavigationSystem.h"
#include "PLGGenerator.h"
#include "PLGLayout.h"

namespace
{
	/** Survives the map restarts between rounds (same process). */
	struct FRunState
	{
		int32 Round = 0;
		int32 Passed = 0;
		int32 Failed = 0;
		uint32 FirstHash = 0;
		int32 FirstSeed = 0;
	};
	FRunState GRun;

	constexpr float DollStandZ = 57.f;    // capsule center above the floor, standing
	constexpr float DollCrouchZ = 32.f;   // crouched
	constexpr float ButlerZ = 96.f;
}

ADMProcSelfTest::ADMProcSelfTest()
{
	PrimaryActorTick.bCanEverTick = true;
}

void ADMProcSelfTest::BeginPlay()
{
	Super::BeginPlay();
	Round = GRun.Round;
	if (UDMNoiseSubsystem* Noise = GetWorld()->GetSubsystem<UDMNoiseSubsystem>())
	{
		NoiseHandle = Noise->OnNoise.AddUObject(this, &ADMProcSelfTest::OnNoise);
	}
	Info(FString::Printf(TEXT("round %d, command line: %s"), Round + 1, FCommandLine::Get()));

	FString Expect;
	if (FParse::Value(FCommandLine::Get(), TEXT("dmprocexpect="), Expect) && Expect == TEXT("abort"))
	{
		AddAbortSteps();
		return;
	}
	if (Expect == TEXT("deskhide"))
	{
		AddDeskHideSteps();
		return;
	}
	switch (Round)
	{
	case 0: AddRoundOneSteps(); break;
	case 1: AddRoundTwoSteps(); break;
	default: AddRoundThreeSteps(); break;
	}
}

void ADMProcSelfTest::EndPlay(const EEndPlayReason::Type EndPlayReason)
{
	if (UDMNoiseSubsystem* Noise = GetWorld()->GetSubsystem<UDMNoiseSubsystem>())
	{
		Noise->OnNoise.Remove(NoiseHandle);
	}
	Super::EndPlay(EndPlayReason);
}

void ADMProcSelfTest::Tick(float DeltaSeconds)
{
	Super::Tick(DeltaSeconds);
	if (bFinished || !Steps.IsValidIndex(Current))
	{
		return;
	}
	// Run sees age 0 on a step's first tick, so steps can set themselves up then.
	FStep& Step = Steps[Current];
	const bool bDone = Step.Run(StepAge);
	StepAge += DeltaSeconds;
	if (bDone)
	{
		++Current;
		StepAge = 0.f;
	}
	else if (StepAge > Step.Timeout)
	{
		Check(false, Step.Name, FString::Printf(TEXT("timed out after %.1f s"), Step.Timeout));
		if (Step.bFatal)
		{
			Finish();
			return;
		}
		++Current;
		StepAge = 0.f;
	}
	if (!Steps.IsValidIndex(Current))
	{
		Finish();
	}
}

void ADMProcSelfTest::Add(const TCHAR* Name, float Timeout, TFunction<bool(float)> Run, bool bFatal)
{
	FStep& Step = Steps.AddDefaulted_GetRef();
	Step.Name = FString::Printf(TEXT("r%d.%s"), Round + 1, Name);
	Step.Timeout = Timeout;
	Step.bFatal = bFatal;
	Step.Run = MoveTemp(Run);
}

void ADMProcSelfTest::Check(bool bPassed, const FString& Name, const FString& Detail)
{
	(bPassed ? GRun.Passed : GRun.Failed)++;
	const bool bPrefixed = Name.Len() > 2 && Name[0] == TEXT('r') && FChar::IsDigit(Name[1]) && Name[2] == TEXT('.');
	const FString Full = bPrefixed ? Name : FString::Printf(TEXT("r%d.%s"), Round + 1, *Name);
	UE_LOG(LogDreamMovers, Display, TEXT("[DMTest] %s %s%s%s"), bPassed ? TEXT("PASS") : TEXT("FAIL"), *Full, Detail.IsEmpty() ? TEXT("") : TEXT(": "), *Detail);
}

void ADMProcSelfTest::Info(const FString& Text)
{
	UE_LOG(LogDreamMovers, Display, TEXT("[DMTest] INFO %s"), *Text);
}

void ADMProcSelfTest::Finish()
{
	if (bFinished)
	{
		return;
	}
	bFinished = true;
	UE_LOG(LogDreamMovers, Display, TEXT("[DMTest] RESULT %s: %d passed, %d failed"), GRun.Failed == 0 ? TEXT("PASS") : TEXT("FAIL"), GRun.Passed, GRun.Failed);
	// Forced: a normal exit request ignores the return code in the editor executable. The log is flushed first.
	FPlatformMisc::RequestExitWithStatus(true, GRun.Failed == 0 ? 0 : 1, TEXT("DreamMovers proc self test finished"));
}

// ------------------------------------------------------------------------------------------------ helpers

ADMCharacter* ADMProcSelfTest::Doll() const
{
	const APlayerController* PC = GetWorld()->GetFirstPlayerController();
	return PC ? Cast<ADMCharacter>(PC->GetPawn()) : nullptr;
}

ADMGameState* ADMProcSelfTest::State() const
{
	return GetWorld()->GetGameState<ADMGameState>();
}

ADMGameMode* ADMProcSelfTest::Mode() const
{
	return GetWorld()->GetAuthGameMode<ADMGameMode>();
}

APLGGenerator* ADMProcSelfTest::Generator() const
{
	TActorIterator<APLGGenerator> It(GetWorld());
	return It ? *It : nullptr;
}

ADMButler* ADMProcSelfTest::Butler() const
{
	TActorIterator<ADMButler> It(GetWorld());
	return It ? *It : nullptr;
}

ADMBellHorse* ADMProcSelfTest::Horse() const
{
	TActorIterator<ADMBellHorse> It(GetWorld());
	return It ? *It : nullptr;
}

void ADMProcSelfTest::SetMonstersActive(bool bActive)
{
	for (TActorIterator<ADMMonster> It(GetWorld()); It; ++It)
	{
		It->SetActorTickEnabled(bActive);
		if (!bActive)
		{
			if (AAIController* AI = Cast<AAIController>(It->GetController()))
			{
				AI->StopMovement();
			}
		}
	}
}

void ADMProcSelfTest::ParkHorse()
{
	ADMBellHorse* H = Horse();
	APLGGenerator* Gen = Generator();
	if (!H || !Gen)
	{
		return;
	}
	if (AAIController* AI = Cast<AAIController>(H->GetController()))
	{
		AI->StopMovement();
	}
	const TArray<FPLGSpawnSlot> Posts = Gen->GetSpawnSlots(TEXT("Guard"));
	if (Posts.Num() > 0)
	{
		H->TeleportTo(Posts[0].Transform.GetLocation() + FVector(0.f, 0.f, 90.f), H->GetActorRotation(), false, true);
	}
}

float ADMProcSelfTest::FloorZ() const
{
	const APLGGenerator* Gen = Generator();
	return Gen ? Gen->GetActorLocation().Z : 0.f;
}

bool ADMProcSelfTest::PlaceDoll(const FVector& Location, const FVector& LookAt)
{
	ADMCharacter* D = Doll();
	if (!D)
	{
		return false;
	}
	const FRotator Facing = (LookAt - Location).Rotation();
	const bool bMoved = D->TeleportTo(Location, FRotator(0.f, Facing.Yaw, 0.f), false, true);
	D->GetCharacterMovement()->StopMovementImmediately();
	if (AController* C = D->GetController())
	{
		C->SetControlRotation(FRotator(0.f, Facing.Yaw, 0.f));
	}
	return bMoved;
}

bool ADMProcSelfTest::PlaceMonster(AActor* Monster, const FVector& Location, const FVector& LookAt)
{
	if (!Monster)
	{
		return false;
	}
	const FRotator Facing = (LookAt - Location).Rotation();
	return Monster->TeleportTo(Location, FRotator(0.f, Facing.Yaw, 0.f), false, true);
}

void ADMProcSelfTest::AimAt(const FVector& Point)
{
	ADMCharacter* D = Doll();
	if (AController* C = D ? D->GetController() : nullptr)
	{
		C->SetControlRotation((Point - D->GetPawnViewLocation()).Rotation());
	}
}

void ADMProcSelfTest::TeleportItem(ADMValuable* Item, const FVector& Location)
{
	if (!Item)
	{
		return;
	}
	UStaticMeshComponent* Mesh = Item->GetMesh();
	Mesh->SetWorldLocationAndRotation(Location, FRotator::ZeroRotator, false, nullptr, ETeleportType::TeleportPhysics);
	Mesh->SetPhysicsLinearVelocity(FVector::ZeroVector);
	Mesh->SetPhysicsAngularVelocityInDegrees(FVector::ZeroVector);
}

bool ADMProcSelfTest::FindStandSpot(const ADMValuable* Item, FVector& OutSpot) const
{
	const ADMCharacter* D = Doll();
	if (!Item || !D)
	{
		return false;
	}
	const FVector Center = Item->GetActorLocation();
	FCollisionQueryParams Params(SCENE_QUERY_STAT(DMTestSpot), false, D);
	const FCollisionShape Capsule = FCollisionShape::MakeCapsule(D->GetCapsuleComponent()->GetUnscaledCapsuleRadius(), D->GetCapsuleComponent()->GetUnscaledCapsuleHalfHeight());
	for (const float Distance : { 130.f, 170.f, 220.f })
	{
		for (int32 Step = 0; Step < 16; ++Step)
		{
			const FVector Dir = FRotator(0.f, Step * 22.5f, 0.f).Vector();
			const FVector Spot(Center.X + Dir.X * Distance, Center.Y + Dir.Y * Distance, FloorZ() + DollStandZ);
			if (!IsInsideHouse(Spot) || GetWorld()->OverlapBlockingTestByChannel(Spot, FQuat::Identity, ECC_Pawn, Capsule, Params))
			{
				continue;
			}
			FHitResult Floor;
			if (!GetWorld()->LineTraceSingleByChannel(Floor, Spot, Spot - FVector(0.f, 0.f, 100.f), ECC_Visibility, Params))
			{
				continue;
			}
			FHitResult View;
			const FVector Eye = Spot + FVector(0.f, 0.f, D->BaseEyeHeight);
			if (GetWorld()->LineTraceSingleByChannel(View, Eye, Center, ECC_Visibility, Params) && View.GetActor() != Item)
			{
				continue;
			}
			OutSpot = Spot;
			return true;
		}
	}
	return false;
}

bool ADMProcSelfTest::IsInsideHouse(const FVector& WorldPoint) const
{
	const APLGGenerator* Gen = Generator();
	if (!Gen)
	{
		return false;
	}
	const FVector Local = Gen->GetActorTransform().InverseTransformPosition(WorldPoint);
	const FPLGLayout& Layout = Gen->GetLayout();
	for (const FPLGPlacedModule& Placed : Layout.Modules)
	{
		if (!Placed.bRemoved && PLG::Footprint(Gen->GetModules()[Placed.Def], Placed).IsInside(FVector2D(Local.X, Local.Y)))
		{
			return true;
		}
	}
	return false;
}

void ADMProcSelfTest::OnNoise(const FVector& Location, float Radius, AActor* Source)
{
	NoisesFromDoll += (Source && Source == Doll()) ? 1 : 0;
	NoisesFromWardrobe += (Source && Source == Wardrobe.Get()) ? 1 : 0;
}

// ------------------------------------------------------------------------------------------------ steps

void ADMProcSelfTest::AddWaitForRound()
{
	Add(TEXT("round_starts_when_ready"), 40.f, [this](float)
	{
		const ADMGameState* GS = State();
		if (!GS)
		{
			return false;
		}
		if (GS->GetRoundState() == EDMRoundState::Preparing)
		{
			bSawPreparing = true;
			bMonstersBeforeReady |= Butler() != nullptr || Horse() != nullptr;
			bTimerBeforeReady |= GS->GetRemainingSeconds() > 0.f;
			return false;
		}
		if (GS->GetRoundState() != EDMRoundState::Playing)
		{
			Check(false, TEXT("round_starts_when_ready"), FString::Printf(TEXT("round state %d instead of Playing (%s)"), int32(GS->GetRoundState()), Mode() ? *Mode()->GetAbortReason() : TEXT("")));
			return true;
		}
		FString Why;
		const bool bNavReady = Generator() && Generator()->IsNavigationReady(Why);
		Check(bSawPreparing && bNavReady, TEXT("round_starts_when_ready"),
			FString::Printf(TEXT("saw Preparing %d, navmesh ready at start %d %s"), bSawPreparing ? 1 : 0, bNavReady ? 1 : 0, *Why));
		Check(!bMonstersBeforeReady && !bTimerBeforeReady, TEXT("no_timer_or_monsters_while_preparing"),
			FString::Printf(TEXT("monsters %d, timer %d"), bMonstersBeforeReady ? 1 : 0, bTimerBeforeReady ? 1 : 0));
		return true;
	}, true);
}

void ADMProcSelfTest::AddAbortSteps()
{
	Add(TEXT("abort_broken_map"), 40.f, [this](float Age)
	{
		const ADMGameState* GS = State();
		if (!GS)
		{
			return false;
		}
		bTimerBeforeReady |= GS->GetRoundState() == EDMRoundState::Playing || GS->GetRemainingSeconds() > 0.f;
		if (GS->GetRoundState() != EDMRoundState::Aborted)
		{
			return false;
		}
		const FString Reason = Mode() ? Mode()->GetAbortReason() : FString();
		Check(true, TEXT("round_aborted"), Reason);
		Check(!bTimerBeforeReady, TEXT("round_never_started"), TEXT("never Playing, timer never ran"));
		Check(Butler() == nullptr && Horse() == nullptr, TEXT("no_monsters"));
		int32 Antiques = 0;
		for (TActorIterator<ADMValuable> It(GetWorld()); It; ++It)
		{
			++Antiques;
		}
		const bool bGenerationFailed = Generator() && !Generator()->IsGenerated();
		Check(!bGenerationFailed || Antiques == 0, TEXT("no_content_without_layout"), FString::Printf(TEXT("generation failed %d, antiques %d"), bGenerationFailed ? 1 : 0, Antiques));
		return true;
	}, true);
	Add(TEXT("abort_stays_aborted"), 6.f, [this](float Age)
	{
		const ADMGameState* GS = State();
		if (!GS || GS->GetRoundState() != EDMRoundState::Aborted)
		{
			Check(false, TEXT("abort_stays_aborted"), TEXT("left the Aborted state"));
			return true;
		}
		if (Age < 4.f)
		{
			return false;
		}
		Check(true, TEXT("abort_stays_aborted"), TEXT("still Aborted 4 s later, no restart"));
		return true;
	});
}

void ADMProcSelfTest::AddDeskHideSteps()
{
	AddWaitForRound();
	Add(TEXT("desk_setup"), 1.f, [this](float)
	{
		IConsoleVariable* TraceVar = IConsoleManager::Get().FindConsoleVariable(TEXT("dm.ButlerTrace"));
		if (TraceVar)
		{
			TraceVar->Set(1);
		}
		APLGGenerator* Gen = Generator();
		const FPLGLayout& Layout = Gen->GetLayout();
		for (int32 i = 0; i < Layout.Modules.Num() && DeskRoom == INDEX_NONE; ++i)
		{
			if (!Layout.Modules[i].bRemoved && Gen->GetModules()[Layout.Modules[i].Def].Name == TEXT("RoomLarge"))
			{
				DeskRoom = i;
			}
		}
		Check(DeskRoom != INDEX_NONE && TraceVar, TEXT("desk_setup"), FString::Printf(TEXT("RoomLarge module %d, trace %d"), DeskRoom, TraceVar ? 1 : 0));
		SetMonstersActive(false);
		ParkHorse();
		return true;
	}, true);
	// RoomLarge desk: center (300, -410), 150 x 80, open room toward +Y and -X. Doll radius 28: an offset of
	// 10 (front) or 45 (side) keeps the whole body under the top; 25 at the front leaves 13 cm sticking out.
	AddDeskHideCase(TEXT("front_center"), FVector2D(0.f, 1.f), FVector2D(0.f, 0.f));
	AddDeskHideCase(TEXT("front_inner_edge"), FVector2D(0.f, 1.f), FVector2D(0.f, 10.f));
	AddDeskHideCase(TEXT("side_center"), FVector2D(-1.f, 0.f), FVector2D(0.f, 0.f));
	AddDeskHideCase(TEXT("side_inner_edge"), FVector2D(-1.f, 0.f), FVector2D(-45.f, 0.f));
	AddDeskHideCase(TEXT("front_center_far"), FVector2D(0.f, 1.f), FVector2D(0.f, 0.f), true);
	AddFencedCase();
	// Last: the doll may be caught, which ends a solo round.
	AddDeskHideCase(TEXT("front_exposed"), FVector2D(0.f, 1.f), FVector2D(0.f, 25.f), false, true);
}

void ADMProcSelfTest::AddDeskHideCase(const TCHAR* Name, const FVector2D& ApproachDir, const FVector2D& HideOffset, bool bBlocked, bool bExposed)
{
	const FString CaseName(Name);
	Add(*FString::Printf(TEXT("desk_%s"), Name), 90.f, [this, CaseName, ApproachDir, HideOffset, bBlocked, bExposed](float Age)
	{
		ADMCharacter* D = Doll();
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		if (!D && CasePhase == 3)
		{
			// Caught while hiding: right for an exposed spot, a failure for a covered one. A solo round ends here.
			const double Since = GetWorld()->GetTimeSeconds() - PhaseStart;
			Check(bExposed, FString::Printf(TEXT("desk_%s_%s"), *CaseName, bExposed ? TEXT("exposed_doll_is_caught") : TEXT("not_caught")),
				FString::Printf(TEXT("caught %.1f s after hiding %s"), Since, *CaseLog));
			Finish();
			return true;
		}
		if (!D || !B || !Gen || DeskRoom == INDEX_NONE)
		{
			Check(false, FString::Printf(TEXT("desk_%s"), *CaseName), TEXT("missing doll, butler or desk room"));
			return true;
		}
		if (Age <= 0.f)
		{
			CasePhase = 0;
			ButlerAtPatrol = FVector::ZeroVector;
		}
		const FPLGPropBox& Board = Gen->GetModules()[Gen->GetLayout().Modules[DeskRoom].Def].Props[0];
		const FTransform T = Gen->GetModuleTransform(DeskRoom);
		auto At = [&T, this](const FVector2D& Local, float Up) { return T.TransformPosition(FVector(Local, 0.f)) + FVector(0.f, 0.f, FloorZ() + Up); };
		const FVector2D Center(Board.Center.X, Board.Center.Y);
		const FVector2D HideLocal = Center + HideOffset;
		const double Now = GetWorld()->GetTimeSeconds();
		const EDMMonsterStateMirror State = EDMMonsterStateMirror(int32(B->GetMonsterState()));
		auto Phase = [this, Now](int32 Next) { CasePhase = Next; PhaseStart = Now; };
		auto Note = [this, Now, &CaseName](const FString& Text)
		{
			CaseLog += FString::Printf(TEXT(" | %.1f %s"), Now - PhaseStart, *Text);
			Info(FString::Printf(TEXT("desk_%s: %s"), *CaseName, *Text));
		};
		if (State != LastButlerState)
		{
			static const TCHAR* Names[] = { TEXT("Idle"), TEXT("Patrol"), TEXT("Chase"), TEXT("Charge"), TEXT("Search"), TEXT("Return"), TEXT("?") };
			Info(FString::Printf(TEXT("desk_%s: butler %s at %s, %.0f cm from the hide spot"), *CaseName, Names[FMath::Min(int32(State), 6)],
				*B->GetActorLocation().ToCompactString(), FVector::Dist2D(B->GetActorLocation(), At(HideLocal, 0.f))));
			LastButlerState = State;
		}

		switch (CasePhase)
		{
		case 0: // Doll in the open in front of the desk, butler further out facing it.
		{
			SetMonstersActive(false);
			if (D->bIsCrouched)
			{
				D->UnCrouch();
				return false;
			}
			const FVector Open = At(HideLocal + ApproachDir * 250.f, DollStandZ);
			const FVector ButlerAt = At(Center + ApproachDir * 600.f, ButlerZ);
			PlaceDoll(Open, ButlerAt);
			PlaceMonster(B, ButlerAt, Open);
			if (bBlocked && !Blocker.IsValid())
			{
				// 60 cm high, across the whole room, 310 cm in front of the desk: the butler stops behind it.
				const FVector2D Perp(ApproachDir.Y, -ApproachDir.X);
				const FVector BarrierAt = At(Center + ApproachDir * 310.f, 30.f);
				AStaticMeshActor* Box = GetWorld()->SpawnActor<AStaticMeshActor>(BarrierAt, FRotator(0.f, FVector(Perp, 0.f).Rotation().Yaw + T.Rotator().Yaw, 0.f));
				if (Box)
				{
					Box->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
					Box->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")));
					Box->GetStaticMeshComponent()->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
					Box->SetActorScale3D(FVector(14.f, 0.4f, 0.6f));
				}
				Blocker = Box;
			}
			CaseLog.Reset();
			CaseRetries = 0;
			LastButlerState = EDMMonsterStateMirror::Unknown;
			B->SetActorTickEnabled(true);
			Phase(1);
			return false;
		}
		case 1: // Wait to be spotted for real (Chase).
			if (State == EDMMonsterStateMirror::Chase)
			{
				Note(TEXT("spotted"));
				D->Crouch();
				Phase(2);
			}
			else if (Now - PhaseStart > 6.0)
			{
				Check(false, FString::Printf(TEXT("desk_%s_spotted"), *CaseName), TEXT("the butler never spotted the doll in the open"));
				CasePhase = 0;
				return true;
			}
			return false;
		case 2: // Crawl under the desk.
		{
			const FVector Hide = At(HideLocal, DollCrouchZ);
			FVector ToHide = Hide - D->GetActorLocation();
			ToHide.Z = 0.f;
			if (ToHide.Size() > 8.f && Now - PhaseStart < 8.0)
			{
				D->AddMovementInput(ToHide.GetSafeNormal(), 1.f);
				return false;
			}
			if (!D)
			{
				return false;
			}
			const bool bUnder = ToHide.Size() <= 8.f;
			Note(FString::Printf(TEXT("under the desk %d (%.0f cm off the hide spot)"), bUnder ? 1 : 0, ToHide.Size()));
			Check(bUnder, FString::Printf(TEXT("desk_%s_crawled_under"), *CaseName), FString::Printf(TEXT("%.0f cm off after %.1f s"), ToHide.Size(), Now - PhaseStart));
			CaseHide = D->GetActorLocation();
			Phase(3);
			return false;
		}
		case 3: // Watch the butler: it should give up, search, and go back to patrolling.
		{
			if (!Doll())
			{
				// Caught: right for an exposed spot, a failure for a covered one. Either way the solo round ends here.
				Check(bExposed, FString::Printf(TEXT("desk_%s_%s"), *CaseName, bExposed ? TEXT("exposed_doll_is_caught") : TEXT("not_caught")),
					FString::Printf(TEXT("caught %.1f s after hiding %s"), Now - PhaseStart, *CaseLog));
				Finish();
				return true;
			}
			if (bExposed && Now - PhaseStart > 20.0)
			{
				Check(false, FString::Printf(TEXT("desk_%s_exposed_doll_is_caught"), *CaseName), FString::Printf(TEXT("still not caught after 20 s %s"), *CaseLog));
				Finish();
				return true;
			}
			if (bExposed)
			{
				return false;
			}
			if (State == EDMMonsterStateMirror::Patrol)
			{
				if (ButlerAtPatrol.IsZero())
				{
					ButlerAtPatrol = B->GetActorLocation();
					Note(TEXT("back on patrol"));
				}
				if (FVector::Dist2D(B->GetActorLocation(), ButlerAtPatrol) > 200.f)
				{
					if (Blocker.IsValid())
					{
						Blocker->Destroy();
					}
					Check(true, FString::Printf(TEXT("desk_%s_butler_gives_up_and_patrols"), *CaseName), FString::Printf(TEXT("%s, walked away %.0f cm"), *CaseLog,
						FVector::Dist2D(B->GetActorLocation(), ButlerAtPatrol)));
					ButlerAtPatrol = FVector::ZeroVector;
					Phase(4);
				}
			}
			else if (Now - PhaseStart > 35.0)
			{
				Check(false, FString::Printf(TEXT("desk_%s_butler_gives_up_and_patrols"), *CaseName), FString::Printf(TEXT("still %s after 35 s, %.0f cm from the doll %s"),
					State == EDMMonsterStateMirror::Chase ? TEXT("chasing") : State == EDMMonsterStateMirror::Search ? TEXT("searching") : TEXT("not patrolling"),
					FVector::Dist2D(B->GetActorLocation(), D->GetActorLocation()), *CaseLog));
				ButlerAtPatrol = FVector::ZeroVector;
				if (Blocker.IsValid())
				{
					Blocker->Destroy();
				}
				Phase(4);
			}
			return false;
		}
		case 4: // Crawl out, stand up, step into the butler's light: it must see us again.
		{
			if (D->bIsCrouched)
			{
				D->UnCrouch();
			}
			// Ahead of where it is walking (it turns with its path), else ahead of where it faces.
			const FVector Moving = B->GetVelocity().GetSafeNormal2D();
			const FVector Facing = Moving.IsNearlyZero() ? B->GetActorForwardVector() : Moving;
			const FVector Spot = B->GetActorLocation() + Facing * 300.f;
			FVector Out(Spot.X, Spot.Y, FloorZ() + DollStandZ);
			if (!IsInsideHouse(Out))
			{
				Out = At(HideLocal + ApproachDir * 250.f, DollStandZ);
			}
			PlaceDoll(Out, B->GetActorLocation());
			CaseOut = Out;
			Phase(5);
			return false;
		}
		case 5:
			if (State == EDMMonsterStateMirror::Chase)
			{
				Check(true, FString::Printf(TEXT("desk_%s_seen_again_after_leaving"), *CaseName), FString::Printf(TEXT("after %.1f s"), Now - PhaseStart));
				SetMonstersActive(false);
				CasePhase = 0;
				return true;
			}
			if (Now - PhaseStart > 2.0 && CaseRetries < 2)
			{
				// Not seen yet: it may have turned at a patrol point. Step in front of it again (three tries in all).
				++CaseRetries;
				CasePhase = 4;
				return false;
			}
			if (Now - PhaseStart > 2.0)
			{
				FString Why;
				B->CanSee(D, nullptr, &Why);
				Check(false, FString::Printf(TEXT("desk_%s_seen_again_after_leaving"), *CaseName), FString::Printf(TEXT("not spotted in three tries, last at %s (%s)"), *CaseOut.ToCompactString(), *Why));
				SetMonstersActive(false);
				CasePhase = 0;
				return true;
			}
			return false;
		default:
			return true;
		}
	});
}

void ADMProcSelfTest::AddFencedCase()
{
	Add(TEXT("fenced_in_open"), 90.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		if (!D || !B || !Gen || DeskRoom == INDEX_NONE)
		{
			Check(false, TEXT("fenced_in_open"), TEXT("missing doll, butler or room"));
			return true;
		}
		const FTransform T = Gen->GetModuleTransform(DeskRoom);
		auto At = [&T, this](const FVector2D& Local, float Up) { return T.TransformPosition(FVector(Local, 0.f)) + FVector(0.f, 0.f, FloorZ() + Up); };
		const double Now = GetWorld()->GetTimeSeconds();
		const EDMMonsterStateMirror State = EDMMonsterStateMirror(int32(B->GetMonsterState()));
		if (Age <= 0.f)
		{
			CasePhase = 0;
		}
		if (State != LastButlerState)
		{
			static const TCHAR* Names[] = { TEXT("Idle"), TEXT("Patrol"), TEXT("Chase"), TEXT("Charge"), TEXT("Search"), TEXT("Return"), TEXT("?") };
			Info(FString::Printf(TEXT("fenced_in_open: butler %s at %s, %.0f cm from the doll"), Names[FMath::Min(int32(State), 6)],
				*B->GetActorLocation().ToCompactString(), FVector::Dist2D(B->GetActorLocation(), D->GetActorLocation())));
			LastButlerState = State;
		}
		// RoomLarge local (0, 0): open floor between the item spots, away from the desk and the wardrobe.
		const FVector DollAt = At(FVector2D(0.f, 0.f), DollStandZ);
		switch (CasePhase)
		{
		case 0:
		{
			SetMonstersActive(false);
			if (D->bIsCrouched)
			{
				D->UnCrouch();
				return false;
			}
			const FVector ButlerAt = At(FVector2D(-450.f, 0.f), ButlerZ);
			PlaceDoll(DollAt, ButlerAt);
			PlaceMonster(B, ButlerAt, DollAt);
			// A 300 x 300 cm pen of 60 cm walls around the doll: the butler sees over it but cannot get in.
			UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));
			const FVector2D Sides[] = { FVector2D(150.f, 0.f), FVector2D(-150.f, 0.f), FVector2D(0.f, 150.f), FVector2D(0.f, -150.f) };
			for (const FVector2D& Side : Sides)
			{
				const bool bAlongY = Side.X != 0.f;
				AStaticMeshActor* Wall = GetWorld()->SpawnActor<AStaticMeshActor>(At(Side, 30.f), T.Rotator());
				if (Wall)
				{
					Wall->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
					Wall->GetStaticMeshComponent()->SetStaticMesh(Cube);
					Wall->GetStaticMeshComponent()->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
					Wall->SetActorScale3D(bAlongY ? FVector(0.2f, 3.2f, 0.6f) : FVector(3.2f, 0.2f, 0.6f));
					Fence.Add(Wall);
				}
			}
			CaseLog.Reset();
			LastButlerState = EDMMonsterStateMirror::Unknown;
			ButlerAtPatrol = FVector::ZeroVector;
			B->SetActorTickEnabled(true);
			CasePhase = 1;
			PhaseStart = Now;
			return false;
		}
		case 1: // Spotted, then stuck at the fence with the doll in plain view.
			if (State == EDMMonsterStateMirror::Chase && CaseLog.IsEmpty())
			{
				CaseLog = FString::Printf(TEXT(" | %.1f spotted"), Now - PhaseStart);
			}
			if (!Doll())
			{
				Check(false, TEXT("fenced_in_open_not_caught"), TEXT("caught through the fence"));
				Finish();
				return true;
			}
			if (State == EDMMonsterStateMirror::Search && !CaseLog.Contains(TEXT("search")))
			{
				FString Why;
				const bool bSees = B->CanSee(D, nullptr, &Why);
				CaseLog += FString::Printf(TEXT(" | %.1f search (still sees the doll %d)"), Now - PhaseStart, bSees ? 1 : 0);
			}
			if (State == EDMMonsterStateMirror::Patrol && !CaseLog.IsEmpty())
			{
				if (ButlerAtPatrol.IsZero())
				{
					ButlerAtPatrol = B->GetActorLocation();
					CaseLog += FString::Printf(TEXT(" | %.1f patrol"), Now - PhaseStart);
				}
				if (FVector::Dist2D(B->GetActorLocation(), ButlerAtPatrol) > 200.f)
				{
					// The point of this case: it gave up while it could still see the doll (out of reach, not out of sight).
					Check(CaseLog.Contains(TEXT("still sees the doll 1")), TEXT("fenced_in_open_butler_gives_up_and_patrols"), FString::Printf(TEXT("%s, walked away %.0f cm"), *CaseLog,
						FVector::Dist2D(B->GetActorLocation(), ButlerAtPatrol)));
					for (const TWeakObjectPtr<AActor>& Wall : Fence)
					{
						if (Wall.IsValid())
						{
							Wall->Destroy();
						}
					}
					Fence.Reset();
					ButlerAtPatrol = FVector::ZeroVector;
					CaseRetries = 0;
					CasePhase = 2;
					PhaseStart = Now;
				}
			}
			else if (Now - PhaseStart > 40.0)
			{
				Check(false, TEXT("fenced_in_open_butler_gives_up_and_patrols"), FString::Printf(TEXT("still state %d after 40 s %s"), int32(State), *CaseLog));
				CasePhase = 9;
			}
			return false;
		case 2: // Out of the pen and into the light again (more than a meter from the spot it gave up on).
		{
			const FVector Moving = B->GetVelocity().GetSafeNormal2D();
			const FVector Facing = Moving.IsNearlyZero() ? B->GetActorForwardVector() : Moving;
			const FVector Spot = B->GetActorLocation() + Facing * 300.f;
			PlaceDoll(FVector(Spot.X, Spot.Y, FloorZ() + DollStandZ), B->GetActorLocation());
			CasePhase = 3;
			PhaseStart = Now;
			return false;
		}
		case 3:
			if (State == EDMMonsterStateMirror::Chase)
			{
				Check(true, TEXT("fenced_in_open_seen_again_after_leaving"), FString::Printf(TEXT("after %.1f s, %.0f cm from the spot it gave up on"),
					Now - PhaseStart, FVector::Dist2D(D->GetActorLocation(), DollAt)));
				SetMonstersActive(false);
				return true;
			}
			if (Now - PhaseStart > 2.0 && CaseRetries < 2)
			{
				++CaseRetries;
				CasePhase = 2;
				return false;
			}
			if (Now - PhaseStart > 2.0)
			{
				FString Why;
				B->CanSee(D, nullptr, &Why);
				Check(false, TEXT("fenced_in_open_seen_again_after_leaving"), Why);
				SetMonstersActive(false);
				return true;
			}
			return false;
		default:
			for (const TWeakObjectPtr<AActor>& Wall : Fence)
			{
				if (Wall.IsValid())
				{
					Wall->Destroy();
				}
			}
			Fence.Reset();
			SetMonstersActive(false);
			return true;
		}
	});
}

void ADMProcSelfTest::AddWaitForRestart(int32 NextRound)
{
	Add(TEXT("restarts"), 25.f, [this, NextRound](float Age)
	{
		GRun.Round = NextRound; // picked up by the test actor of the reloaded map
		return false;           // the map reload destroys this actor; reaching the timeout means it never came
	}, true);
}

void ADMProcSelfTest::AddRoundOneSteps()
{
	AddWaitForRound();

	Add(TEXT("generation"), 1.f, [this](float)
	{
		APLGGenerator* Gen = Generator();
		if (!Gen)
		{
			Check(false, TEXT("generation"), TEXT("no generator in the map"));
			return true;
		}
		Gen->RunGenerationChecks([this](bool bPassed, const FString& What) { Check(bPassed, TEXT("generation"), What); });
		GRun.FirstHash = Gen->GetLayoutHash();
		GRun.FirstSeed = Gen->GetSeed();
		return true;
	});

	Add(TEXT("content"), 1.f, [this](float)
	{
		const ADMGameMode* GM = Mode();
		APLGGenerator* Gen = Generator();
		int32 Count = 0;
		int32 OnFloor = 0;
		int32 Inside = 0;
		FString Bad;
		for (TActorIterator<ADMValuable> It(GetWorld()); It; ++It)
		{
			++Count;
			const FVector Center = It->GetActorLocation();
			FHitResult Hit;
			FCollisionQueryParams Params(SCENE_QUERY_STAT(DMTestFloor), false, *It);
			const bool bHit = GetWorld()->LineTraceSingleByObjectType(Hit, Center, Center - FVector(0.f, 0.f, 300.f), FCollisionObjectQueryParams(ECC_WorldStatic), Params);
			// The physics body is what rests on the floor; the visible mesh can differ (a non-uniformly scaled sphere
			// collides as a sphere of the smallest scale and its visible bottom dips below the floor).
			FVector Lowest;
			const float Bottom = It->GetMesh()->GetClosestPointOnCollision(Center - FVector(0.f, 0.f, 1000.f), Lowest) > 0.f ? Lowest.Z : It->GetMesh()->Bounds.GetBox().Min.Z;
			const float VisibleBottom = It->GetMesh()->Bounds.GetBox().Min.Z;
			if (VisibleBottom < FloorZ() - 2.f)
			{
				Info(FString::Printf(TEXT("%s: visible mesh dips %.1f cm below the floor (physics body rests at %.1f)"), *It->GetDisplayName().ToString(), FloorZ() - VisibleBottom, Bottom));
			}
			// Resting on the generated floor itself: not on a desk, a wardrobe or another item, and not sinking.
			const bool bFloor = bHit && Hit.GetActor() == Gen && Hit.ImpactNormal.Z > 0.9f && FMath::Abs(Hit.ImpactPoint.Z - FloorZ()) < 2.f
				&& FMath::Abs(Bottom - FloorZ()) < 4.f && It->GetVelocity().Size() < 5.f;
			const bool bInside = IsInsideHouse(Center);
			OnFloor += bFloor ? 1 : 0;
			Inside += bInside ? 1 : 0;
			if (!bFloor || !bInside)
			{
				Bad += FString::Printf(TEXT(" %s(z %.1f bottom %.1f floor-hit %d inside %d)"), *It->GetDisplayName().ToString(), Center.Z, Bottom, bHit && Hit.GetActor() == Gen ? 1 : 0, bInside ? 1 : 0);
			}
		}
		const int32 Expected = GM ? GM->GetExpectedAntiques() : -1;
		Check(Expected > 0 && Count == Expected, TEXT("antique_count"), FString::Printf(TEXT("%d in the map, %d expected"), Count, Expected));
		Check(Count > 0 && OnFloor == Count && Inside == Count, TEXT("antiques_on_generated_floor_inside_house"),
			FString::Printf(TEXT("%d/%d on the floor, %d/%d inside%s"), OnFloor, Count, Inside, Count, *Bad));
		Check(GM && GM->GetBlockedAntiqueSpawns() == 0, TEXT("antique_spots_free_at_spawn"), FString::Printf(TEXT("%d spots already blocked"), GM ? GM->GetBlockedAntiqueSpawns() : -1));

		int32 Spots = 0;
		for (TActorIterator<ADMHidingSpot> It(GetWorld()); It; ++It)
		{
			++Spots;
		}
		const int32 HideSlots = Gen ? Gen->GetSpawnSlots(TEXT("HideSmall")).Num() + Gen->GetSpawnSlots(TEXT("HideLarge")).Num() : -1;
		Check(Spots > 0 && Spots == HideSlots, TEXT("wardrobes"), FString::Printf(TEXT("%d wardrobes for %d hiding spots"), Spots, HideSlots));
		const ADMButler* B = Butler();
		const ADMBellHorse* H = Horse();
		Check(B && B->GetController() && H && H->GetController(), TEXT("monsters_with_ai"));

		const ADMGameState* GS = State();
		const int32 WantTarget = GM ? FMath::RoundToInt(GM->GetMapValue() * 0.3f) : -1;
		Check(GS && GS->GetTargetValue() == WantTarget && WantTarget > 0, TEXT("solo_target_30_percent"),
			FString::Printf(TEXT("target %d, map value %d, 30%% = %d"), GS ? GS->GetTargetValue() : -1, GM ? GM->GetMapValue() : -1, WantTarget));
		SetMonstersActive(false);
		return true;
	});

	// ---- wardrobe in, checks while hidden
	Add(TEXT("wardrobe_enter"), 2.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		if (!Wardrobe.IsValid())
		{
			TActorIterator<ADMHidingSpot> It(GetWorld());
			Wardrobe = It ? *It : nullptr;
			if (!Wardrobe.IsValid() || !D)
			{
				Check(false, TEXT("wardrobe_enter"), TEXT("no wardrobe or no doll"));
				return true;
			}
			const FTransform T = Wardrobe->GetActorTransform();
			PlaceDoll(T.TransformPosition(FVector(Wardrobe->GetSize().X * 0.5f + 50.f, 0.f, DollStandZ)), T.GetLocation() + FVector(0.f, 0.f, DollStandZ));
			return false;
		}
		if (Age < 0.3f)
		{
			return false;
		}
		NoisesFromWardrobe = 0;
		D->ServerInteract();
		Check(D->IsHiding() && D->GetHidingSpot() == Wardrobe.Get() && Wardrobe->GetNumOccupants() == 1, TEXT("wardrobe_enter"),
			FString::Printf(TEXT("hiding %d, occupants %d, door noise %d"), D->IsHiding() ? 1 : 0, Wardrobe->GetNumOccupants(), NoisesFromWardrobe));
		Check(NoisesFromWardrobe > 0, TEXT("wardrobe_enter_makes_noise"));
		HiddenAt = D->GetActorLocation();

		ADMButler* B = Butler();
		Check(B && !B->CanSee(D) && !B->CanCatch(D), TEXT("hidden_doll_unseen_and_uncatchable"));
		ADMValuable* Any = nullptr;
		for (TActorIterator<ADMValuable> It(GetWorld()); It && !Any; ++It)
		{
			Any = *It;
		}
		if (Any)
		{
			D->GetGrab()->ServerGrab(Any, Any->GetActorLocation());
			Check(D->GetGrab()->GetHeldValuable() == nullptr, TEXT("server_rejects_grab_while_hidden"));
			D->GetGrab()->ServerRelease();
		}
		return true;
	});

	// ---- butler patrol and horse charge, with the doll safe in the wardrobe
	Add(TEXT("butler_patrols"), 70.f, [this](float Age)
	{
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		if (!B || !Gen)
		{
			Check(false, TEXT("butler_patrols"), TEXT("no butler"));
			return true;
		}
		if (Age <= KINDA_SMALL_NUMBER || ButlerStart.IsZero())
		{
			SetMonstersActive(true);
			ButlerStart = LastButlerLocation = B->GetActorLocation();
			ButlerVisited.Reset();
			ButlerTravel = 0.f;
		}
		ButlerTravel += FVector::Dist2D(B->GetActorLocation(), LastButlerLocation);
		LastButlerLocation = B->GetActorLocation();
		const FPLGLayout& Layout = Gen->GetLayout();
		for (int32 i = 0; i < Layout.Modules.Num(); ++i)
		{
			const EPLGRole Role = Gen->GetModules()[Layout.Modules[i].Def].Role;
			if (!Layout.Modules[i].bRemoved && (Role == EPLGRole::Room || Role == EPLGRole::Hub)
				&& FVector::Dist2D(B->GetActorLocation(), Gen->GetModuleTransform(i).GetLocation()) < 200.f)
			{
				ButlerVisited.Add(i);
			}
		}
		if (ButlerVisited.Num() >= 2 && ButlerTravel > 500.f)
		{
			Check(true, TEXT("butler_patrols"), FString::Printf(TEXT("reached %d patrol rooms, walked %.0f cm in %.1f s"), ButlerVisited.Num(), ButlerTravel, Age));
			return true;
		}
		if (Age > 68.f)
		{
			Check(false, TEXT("butler_patrols"), FString::Printf(TEXT("reached %d patrol rooms, walked %.0f cm"), ButlerVisited.Num(), ButlerTravel));
			return true;
		}
		return false;
	});

	Add(TEXT("horse_runs_to_noise"), 25.f, [this](float Age)
	{
		ADMBellHorse* H = Horse();
		APLGGenerator* Gen = Generator();
		if (!H || !Gen)
		{
			Check(false, TEXT("horse_runs_to_noise"), TEXT("no horse"));
			return true;
		}
		if (!bNoiseSent)
		{
			bNoiseSent = true;
			// The start module is never the hub, and its pivot is open floor.
			NoiseAt = Gen->GetModuleTransform(0).GetLocation();
			bHorseCharged = false;
			UDMNoiseSubsystem::Report(this, NoiseAt, 4000.f, this);
			bHorseCharged = H->GetMonsterState() == EDMMonsterState::Charge;
			Info(FString::Printf(TEXT("noise at %s, horse %.0f cm away, state %d"), *NoiseAt.ToCompactString(), FVector::Dist2D(H->GetActorLocation(), NoiseAt), int32(H->GetMonsterState())));
			return false;
		}
		const float Distance = FVector::Dist2D(H->GetActorLocation(), NoiseAt);
		if (Distance < 200.f)
		{
			Check(bHorseCharged, TEXT("horse_runs_to_noise"), FString::Printf(TEXT("charged at once %d, within %.0f cm after %.1f s"), bHorseCharged ? 1 : 0, Distance, Age));
			return true;
		}
		if (Age > 23.f)
		{
			Check(false, TEXT("horse_runs_to_noise"), FString::Printf(TEXT("still %.0f cm away, state %d"), Distance, int32(H->GetMonsterState())));
			return true;
		}
		return false;
	});

	// ---- blocked wardrobe exit, then a free one
	Add(TEXT("wardrobe_blocked_exit"), 2.f, [this](float Age)
	{
		SetMonstersActive(false);
		ParkHorse();
		ADMCharacter* D = Doll();
		if (!D || !Wardrobe.IsValid() || !D->IsHiding())
		{
			Check(false, TEXT("wardrobe_blocked_exit"), TEXT("doll is not in the wardrobe"));
			return true;
		}
		if (!Blocker.IsValid())
		{
			const FTransform T = Wardrobe->GetActorTransform();
			const FVector Size = Wardrobe->GetSize();
			const FVector Center = T.TransformPosition(FVector(Size.X * 0.5f + 85.f, 0.f, 100.f));
			AStaticMeshActor* Box = GetWorld()->SpawnActor<AStaticMeshActor>(Center, T.Rotator());
			if (Box)
			{
				Box->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable);
				Box->GetStaticMeshComponent()->SetStaticMesh(LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")));
				Box->GetStaticMeshComponent()->SetCollisionProfileName(UCollisionProfile::BlockAll_ProfileName);
				Box->SetActorScale3D(FVector(1.5f, (Size.Y + 220.f) / 100.f, 2.f));
			}
			Blocker = Box;
			return false;
		}
		if (Age < 0.2f)
		{
			return false;
		}
		D->ServerInteract();
		Check(D->IsHiding() && Wardrobe->GetNumOccupants() == 1 && FVector::Dist(D->GetActorLocation(), HiddenAt) < 1.f, TEXT("wardrobe_blocked_exit_stays_hidden"),
			FString::Printf(TEXT("hiding %d, occupants %d, moved %.1f cm"), D->IsHiding() ? 1 : 0, Wardrobe->GetNumOccupants(), FVector::Dist(D->GetActorLocation(), HiddenAt)));
		Blocker->Destroy();
		return true;
	});

	Add(TEXT("wardrobe_exit"), 2.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		if (Age < 0.2f || !D || !Wardrobe.IsValid())
		{
			return Age >= 0.2f;
		}
		NoisesFromWardrobe = 0;
		D->ServerInteract();
		const FVector Local = Wardrobe->GetActorTransform().InverseTransformPosition(D->GetActorLocation());
		Check(!D->IsHiding() && Wardrobe->GetNumOccupants() == 0 && D->GetActorEnableCollision()
			&& D->GetCharacterMovement()->MovementMode == MOVE_Walking && Local.X > Wardrobe->GetSize().X * 0.5f, TEXT("wardrobe_exit"),
			FString::Printf(TEXT("hiding %d, occupants %d, collision %d, %.0f cm in front, door noise %d"), D->IsHiding() ? 1 : 0, Wardrobe->GetNumOccupants(),
				D->GetActorEnableCollision() ? 1 : 0, Local.X - Wardrobe->GetSize().X * 0.5f, NoisesFromWardrobe));
		return true;
	});

	// ---- catching: walls and tabletops shield, open ground does not (monsters frozen, positions exact)
	Add(TEXT("catch_geometry"), 1.f, [this](float)
	{
		ADMCharacter* D = Doll();
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		if (!D || !B || !Gen)
		{
			Check(false, TEXT("catch_geometry"), TEXT("missing doll, butler or generator"));
			return true;
		}
		SetMonstersActive(false);
		// The start module's +X wall: the hub is always behind it, and the door is in the middle, so y = -300 is solid wall.
		const FTransform Start = Gen->GetModuleTransform(0);
		const float Z = FloorZ();
		auto At = [&Start, Z](float X, float Y, float Up) { return Start.TransformPosition(FVector(X, Y, 0.f)) + FVector(0.f, 0.f, Up); };

		PlaceDoll(At(455.f, -300.f, DollStandZ), At(545.f, -300.f, DollStandZ));
		PlaceMonster(B, At(545.f, -300.f, ButlerZ), D->GetActorLocation());
		Check(!B->CanCatch(D), TEXT("no_catch_through_wall_doll_inside"), FString::Printf(TEXT("%.0f cm apart across the wall"), FVector::Dist2D(D->GetActorLocation(), B->GetActorLocation())));
		PlaceDoll(At(545.f, -300.f, DollStandZ), At(455.f, -300.f, DollStandZ));
		PlaceMonster(B, At(455.f, -300.f, ButlerZ), D->GetActorLocation());
		Check(!B->CanCatch(D), TEXT("no_catch_through_wall_doll_outside"), FString::Printf(TEXT("%.0f cm apart across the wall"), FVector::Dist2D(D->GetActorLocation(), B->GetActorLocation())));
		PlaceDoll(At(455.f, -300.f, DollStandZ), At(365.f, -300.f, DollStandZ));
		PlaceMonster(B, At(365.f, -300.f, ButlerZ), D->GetActorLocation());
		Check(B->CanCatch(D), TEXT("catch_in_the_open"), FString::Printf(TEXT("%.0f cm apart, nothing between"), FVector::Dist2D(D->GetActorLocation(), B->GetActorLocation())));
		PlaceDoll(At(-300.f, -300.f, DollStandZ), At(0.f, 0.f, DollStandZ));
		return true;
	});

	Add(TEXT("crouch_for_desk"), 2.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		if (!D)
		{
			return false;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			D->Crouch();
		}
		return D->bIsCrouched != 0;
	});

	Add(TEXT("desk_shields"), 1.f, [this](float)
	{
		ADMCharacter* D = Doll();
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		const FPLGLayout& Layout = Gen->GetLayout();
		int32 Room = INDEX_NONE;
		for (int32 i = 0; i < Layout.Modules.Num() && Room == INDEX_NONE; ++i)
		{
			if (!Layout.Modules[i].bRemoved && Gen->GetModules()[Layout.Modules[i].Def].Props.Num() > 0)
			{
				Room = i;
			}
		}
		if (Room == INDEX_NONE || !D || !B)
		{
			Check(false, TEXT("desk_shields"), TEXT("no room with a desk"));
			return true;
		}
		// First prop box is the tabletop; the room's open side is toward +Y in module space for the sample kit.
		const FPLGPropBox& Board = Gen->GetModules()[Layout.Modules[Room].Def].Props[0];
		const FTransform T = Gen->GetModuleTransform(Room);
		auto At = [&T](float X, float Y, float Up) { return T.TransformPosition(FVector(X, Y, 0.f)) + FVector(0.f, 0.f, Up); };
		const float Edge = Board.Size.Y * 0.5f;
		const FVector Under = At(Board.Center.X, Board.Center.Y, DollCrouchZ);

		PlaceDoll(Under, At(Board.Center.X, Board.Center.Y + 300.f, DollCrouchZ));
		PlaceMonster(B, At(Board.Center.X, Board.Center.Y + Edge + 35.f, ButlerZ), Under);
		Check(!B->CanCatch(D), TEXT("no_catch_under_desk"), FString::Printf(TEXT("butler %.0f cm away at the desk edge"), FVector::Dist2D(D->GetActorLocation(), B->GetActorLocation())));

		PlaceMonster(B, At(Board.Center.X, Board.Center.Y + Edge + 160.f, ButlerZ), Under);
		Check(!B->CanSee(D), TEXT("desk_blocks_candlelight_at_2m"), TEXT("crouched under the desk, butler 2 m away facing it"));

		PlaceMonster(B, At(Board.Center.X, Board.Center.Y + Edge + 260.f, ButlerZ), Under);
		Info(FString::Printf(TEXT("under the desk from 3 m away (low angle): butler can see the doll = %d"), B->CanSee(D) ? 1 : 0));

		// Control: same butler spot, doll crouched in the open 2 m in front of it.
		const FVector ButlerAt = At(Board.Center.X, Board.Center.Y + Edge + 160.f, ButlerZ);
		const FVector Open = At(Board.Center.X, Board.Center.Y + Edge + 360.f, DollCrouchZ);
		PlaceMonster(B, ButlerAt, Open);
		PlaceDoll(Open, ButlerAt);
		Check(B->CanSee(D), TEXT("crouched_doll_in_open_is_seen_at_2m"), TEXT("control for the desk check"));
		return true;
	});

	// ---- crouch: slow and silent; sprinting standing up is loud
	Add(TEXT("crouch_move"), 4.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		APLGGenerator* Gen = Generator();
		if (!D || !Gen)
		{
			return false;
		}
		const FTransform Start = Gen->GetModuleTransform(0);
		if (Age <= KINDA_SMALL_NUMBER)
		{
			PlaceDoll(Start.TransformPosition(FVector(-400.f, -250.f, DollCrouchZ)), Start.TransformPosition(FVector(500.f, -250.f, DollCrouchZ)));
			D->SetSprinting(true);
			NoisesFromDoll = 0;
			return false;
		}
		// Count from once the doll has settled after the teleport (a 2 cm drop lands with a thud).
		if (Age < 0.3f)
		{
			NoisesFromDoll = 0;
		}
		D->AddMovementInput(Start.GetRotation().GetForwardVector(), 1.f);
		if (Age < 1.8f)
		{
			return false;
		}
		const float Speed = D->GetVelocity().Size2D();
		Check(D->bIsCrouched && FMath::IsNearlyEqual(D->GetCapsuleComponent()->GetUnscaledCapsuleHalfHeight(), 30.f) && Speed <= 160.f && Speed > 50.f && NoisesFromDoll == 0,
			TEXT("crouch_slow_and_silent"), FString::Printf(TEXT("crouched %d, height %.0f cm, speed %.0f, footstep noises %d"), D->bIsCrouched ? 1 : 0,
				D->GetCapsuleComponent()->GetUnscaledCapsuleHalfHeight() * 2.f, Speed, NoisesFromDoll));
		D->UnCrouch();
		return true;
	});

	Add(TEXT("sprint_move"), 5.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		APLGGenerator* Gen = Generator();
		if (!D || !Gen || D->bIsCrouched)
		{
			return false;
		}
		const FTransform Start = Gen->GetModuleTransform(0);
		if (Age < 0.3f)
		{
			PlaceDoll(Start.TransformPosition(FVector(-400.f, -250.f, DollStandZ)), Start.TransformPosition(FVector(500.f, -250.f, DollStandZ)));
			D->SetSprinting(true);
			NoisesFromDoll = 0;
			return false;
		}
		D->AddMovementInput(Start.GetRotation().GetForwardVector(), 1.f);
		if (Age < 1.8f)
		{
			return false;
		}
		Check(NoisesFromDoll > 0, TEXT("sprint_is_loud_control"), FString::Printf(TEXT("footstep noises %d at %.0f cm/s"), NoisesFromDoll, D->GetVelocity().Size2D()));
		D->SetSprinting(false);
		ParkHorse();
		return true;
	});

	// ---- grabbing and carrying a light antique to the extraction zone
	Add(TEXT("grab_light"), 2.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		TActorIterator<ADMExtractionZone> Zone(GetWorld());
		UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
		if (!D || !Zone || !Nav)
		{
			Check(false, TEXT("grab_light"), TEXT("missing doll, zone or navigation"));
			return true;
		}
		if (!Light.IsValid())
		{
			// The light antique with the shortest walk to the zone, and a spot to stand next to it.
			float Best = TNumericLimits<float>::Max();
			FVector BestSpot;
			for (TActorIterator<ADMValuable> It(GetWorld()); It; ++It)
			{
				FVector Spot;
				if (It->GetMassKg() > 10.f || !FindStandSpot(*It, Spot))
				{
					continue;
				}
				const UNavigationPath* Path = Nav->FindPathToLocationSynchronously(GetWorld(), Spot, Zone->GetActorLocation() - FVector(0.f, 0.f, 50.f));
				if (Path && Path->IsValid() && !Path->IsPartial() && Path->GetPathLength() < Best)
				{
					Best = Path->GetPathLength();
					Light = *It;
					BestSpot = Spot;
				}
			}
			if (!Light.IsValid())
			{
				Check(false, TEXT("grab_light"), TEXT("no reachable light antique"));
				return true;
			}
			PlaceDoll(BestSpot, Light->GetActorLocation());
			Info(FString::Printf(TEXT("light antique %s (%.1f kg), %.0f cm walk to the zone"), *Light->GetDisplayName().ToString(), Light->GetMassKg(), Best));
			return false;
		}
		// Aim, then grab a frame later: the grab aims with the camera, which picks up the new rotation next frame.
		if (Age < 0.4f)
		{
			AimAt(Light->GetActorLocation());
			return false;
		}
		D->GetGrab()->TryGrab();
		ItemStartZ = Light->GetActorLocation().Z;
		FVector Point;
		const ADMValuable* Traced = D->GetGrab()->TraceForValuable(&Point);
		Check(D->GetGrab()->GetHeldValuable() == Light.Get(), TEXT("grab_light"), FString::Printf(TEXT("%s, the aim trace finds %s"),
			*Light->GetDisplayName().ToString(), Traced ? *Traced->GetDisplayName().ToString() : TEXT("nothing")));
		return true;
	});

	Add(TEXT("lift_light"), 3.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		if (!D || !Light.IsValid() || D->GetGrab()->GetHeldValuable() != Light.Get())
		{
			Check(false, TEXT("lift_light"), TEXT("not holding it"));
			return true;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			D->GetGrab()->HoldDistance = 110.f;
			if (AController* C = D->GetController())
			{
				C->SetControlRotation(FRotator(0.f, C->GetControlRotation().Yaw, 0.f));
			}
		}
		if (Age < 1.2f)
		{
			return false;
		}
		const float Rise = Light->GetActorLocation().Z - ItemStartZ;
		Check(Rise > 20.f, TEXT("lift_light"), FString::Printf(TEXT("rose %.0f cm"), Rise));
		return true;
	});

	Add(TEXT("carry_light_to_zone"), 40.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		TActorIterator<ADMExtractionZone> Zone(GetWorld());
		UNavigationSystemV1* Nav = FNavigationSystem::GetCurrent<UNavigationSystemV1>(GetWorld());
		if (!D || !Zone || !Nav || !Light.IsValid())
		{
			Check(false, TEXT("carry_light_to_zone"), TEXT("lost the item"));
			return true;
		}
		if (CarryPath.Num() == 0)
		{
			const UNavigationPath* Path = Nav->FindPathToLocationSynchronously(GetWorld(), D->GetActorLocation(), Zone->GetActorLocation() - FVector(0.f, 0.f, 50.f));
			if (!Path || !Path->IsValid())
			{
				Check(false, TEXT("carry_light_to_zone"), TEXT("no path"));
				return true;
			}
			CarryPath = Path->PathPoints;
			CarryIndex = 1;
			return false;
		}
		if (D->GetGrab()->GetHeldValuable() != Light.Get())
		{
			Check(false, TEXT("carry_light_to_zone"), FString::Printf(TEXT("the thread broke at %s"), *D->GetActorLocation().ToCompactString()));
			return true;
		}
		// Walk the path at about walking speed, looking where we go, with the item just ahead.
		const float StepLength = 280.f * GetWorld()->GetDeltaSeconds();
		FVector Here = D->GetActorLocation();
		while (CarryPath.IsValidIndex(CarryIndex) && FVector::Dist2D(Here, CarryPath[CarryIndex]) < StepLength)
		{
			++CarryIndex;
		}
		if (!CarryPath.IsValidIndex(CarryIndex))
		{
			Check(true, TEXT("carry_light_to_zone"), FString::Printf(TEXT("arrived holding it after %.1f s"), Age));
			return true;
		}
		FVector Dir = CarryPath[CarryIndex] - Here;
		Dir.Z = 0.f;
		Dir.Normalize();
		Here += Dir * StepLength;
		Here.Z = FloorZ() + DollStandZ;
		D->SetActorLocation(Here, false, nullptr, ETeleportType::None);
		if (AController* C = D->GetController())
		{
			const FRotator Want(-15.f, Dir.Rotation().Yaw, 0.f);
			C->SetControlRotation(FMath::RInterpTo(C->GetControlRotation(), Want, GetWorld()->GetDeltaSeconds(), 6.f));
		}
		return false;
	});

	Add(TEXT("deliver_light"), 4.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		ADMGameState* GS = State();
		if (!D || !GS)
		{
			return false;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			if (!Light.IsValid() || D->GetGrab()->GetHeldValuable() != Light.Get())
			{
				Check(false, TEXT("deliver_light"), TEXT("not holding the item at the zone"));
				return true;
			}
			// Look down so the item hangs over the bed, then let go.
			if (AController* C = D->GetController())
			{
				C->SetControlRotation(FRotator(-55.f, C->GetControlRotation().Yaw, 0.f));
			}
			return false;
		}
		if (DeliverTime == 0.0 && Age > 0.6f && Light.IsValid())
		{
			DeliverValue = Light->GetCurrentValue();
			CollectedBefore = GS->GetCollectedValue();
			RemainingBefore = GS->GetRemainingSeconds();
			DeliverTime = GetWorld()->GetTimeSeconds();
			D->GetGrab()->Release();
			return false;
		}
		if (DeliverTime == 0.0 || Light.IsValid())
		{
			return false;
		}
		const double Elapsed = GetWorld()->GetTimeSeconds() - DeliverTime;
		const double Drain = (RemainingBefore - GS->GetRemainingSeconds()) - Elapsed;
		const double WantDrain = DeliverValue * 20.0 / 1000.0;
		Check(GS->GetCollectedValue() - CollectedBefore == DeliverValue && DeliverValue > 0, TEXT("deliver_adds_value"),
			FString::Printf(TEXT("collected +%d for an item worth %d"), GS->GetCollectedValue() - CollectedBefore, DeliverValue));
		Check(FMath::Abs(Drain - WantDrain) < 0.35, TEXT("deliver_drains_20s_per_1000"), FString::Printf(TEXT("stability -%.2f s beyond time, expected %.2f s"), Drain, WantDrain));
		return true;
	});

	Add(TEXT("heavy_needs_two"), 4.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		if (!D)
		{
			return false;
		}
		if (!Heavy.IsValid())
		{
			FVector Spot;
			for (TActorIterator<ADMValuable> It(GetWorld()); It && !Heavy.IsValid(); ++It)
			{
				if (It->GetMassKg() > 20.f && FindStandSpot(*It, Spot))
				{
					Heavy = *It;
				}
			}
			if (!Heavy.IsValid())
			{
				Check(false, TEXT("heavy_needs_two"), TEXT("no reachable heavy antique"));
				return true;
			}
			PlaceDoll(Spot, Heavy->GetActorLocation());
			return false;
		}
		if (Age < 0.4f)
		{
			AimAt(Heavy->GetActorLocation());
			return false;
		}
		if (D->GetGrab()->GetHeldValuable() == nullptr && Age < 0.6f)
		{
			D->GetGrab()->TryGrab();
			ItemStartZ = Heavy->GetActorLocation().Z;
			if (AController* C = D->GetController())
			{
				C->SetControlRotation(FRotator(10.f, C->GetControlRotation().Yaw, 0.f));
			}
			return false;
		}
		if (Age < 2.2f)
		{
			return false;
		}
		const bool bHeld = D->GetGrab()->GetHeldValuable() == Heavy.Get();
		const float Rise = Heavy->GetActorLocation().Z - ItemStartZ;
		Check(bHeld && Rise < 25.f, TEXT("solo_cannot_lift_heavy"), FString::Printf(TEXT("%s %.0f kg, held %d, rose %.0f cm"), *Heavy->GetDisplayName().ToString(), Heavy->GetMassKg(), bHeld ? 1 : 0, Rise));
		D->GetGrab()->Release();
		return true;
	});

	// ---- the server checks grab requests: through a wall, a point off the item, and a normal one
	Add(TEXT("server_grab_validation"), 2.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		APLGGenerator* Gen = Generator();
		if (!D || !Gen)
		{
			return false;
		}
		const FTransform Start = Gen->GetModuleTransform(0);
		auto At = [&Start, this](float X, float Y, float Up) { return Start.TransformPosition(FVector(X, Y, 0.f)) + FVector(0.f, 0.f, FloorZ() + Up); };
		if (!Spare.IsValid())
		{
			for (TActorIterator<ADMValuable> It(GetWorld()); It && !Spare.IsValid(); ++It)
			{
				if (It->GetMassKg() <= 10.f && *It != Light.Get())
				{
					Spare = *It;
				}
			}
			if (!Spare.IsValid())
			{
				Check(false, TEXT("server_grab_validation"), TEXT("no spare light antique"));
				return true;
			}
			// Item in the hub right behind the start module's wall, doll on this side.
			TeleportItem(Spare.Get(), At(600.f, -300.f, 40.f));
			PlaceDoll(At(380.f, -300.f, DollStandZ), At(600.f, -300.f, 40.f));
			return false;
		}
		if (Age < 0.5f)
		{
			return false;
		}
		UDMGrabComponent* Grab = D->GetGrab();
		Grab->ServerRelease();
		Grab->ServerGrab(Spare.Get(), Spare->GetActorLocation());
		Check(Grab->GetHeldValuable() == nullptr, TEXT("server_rejects_grab_through_wall"), FString::Printf(TEXT("item %.0f cm away behind the wall"), FVector::Dist(D->GetPawnViewLocation(), Spare->GetActorLocation())));
		Grab->ServerRelease();

		TeleportItem(Spare.Get(), At(470.f, -300.f, 40.f));
		// A point in reach but 1.5 m above the item.
		const FVector OffItem = Spare->GetActorLocation() + FVector(0.f, 0.f, 150.f);
		Grab->ServerGrab(Spare.Get(), OffItem);
		Check(Grab->GetHeldValuable() == nullptr, TEXT("server_rejects_point_off_the_item"), FString::Printf(TEXT("point %.0f cm from the view, 150 cm above the item"), FVector::Dist(D->GetPawnViewLocation(), OffItem)));
		Grab->ServerRelease();

		Grab->ServerGrab(Spare.Get(), Spare->GetActorLocation());
		Check(Grab->GetHeldValuable() == Spare.Get(), TEXT("server_accepts_valid_grab"), FString::Printf(TEXT("item %.0f cm away in plain view"), FVector::Dist(D->GetPawnViewLocation(), Spare->GetActorLocation())));
		Grab->ServerRelease();
		return true;
	});

	// ---- Collapse follows stability
	Add(TEXT("collapse_follows_stability"), 8.f, [this](float Age)
	{
		ADMGameState* GS = State();
		const APlayerController* PC = GetWorld()->GetFirstPlayerController();
		const ADMHUD* HUD = PC ? Cast<ADMHUD>(PC->GetHUD()) : nullptr;
		if (!GS || !HUD)
		{
			Check(false, TEXT("collapse_follows_stability"), TEXT("no HUD"));
			return true;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			Check(GS->GetStability() > 0.5f && HUD->GetCollapse() < 0.02f, TEXT("full_color_above_half_stability"),
				FString::Printf(TEXT("stability %.2f, collapse %.3f"), GS->GetStability(), HUD->GetCollapse()));
			GS->DebugDrainStability(GS->GetRemainingSeconds() - 0.3f * GS->GetRoundDuration());
			return false;
		}
		if (Age < 5.f)
		{
			return false;
		}
		// The HUD maps stability 0.5..0 to collapse 0..0.75.
		const float Want = FMath::GetMappedRangeValueClamped(FVector2f(0.5f, 0.f), FVector2f(0.f, 0.75f), GS->GetStability());
		Check(FMath::Abs(HUD->GetCollapse() - Want) < 0.05f, TEXT("collapse_follows_stability"),
			FString::Printf(TEXT("stability %.2f, collapse %.3f, expected %.3f"), GS->GetStability(), HUD->GetCollapse(), Want));
		return true;
	});

	Add(TEXT("target_reached_succeeds"), 4.f, [this](float Age)
	{
		ADMGameState* GS = State();
		if (!GS)
		{
			return false;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			GS->AddCollected(FMath::Max(0, GS->GetTargetValue() - GS->GetCollectedValue()));
			return false;
		}
		if (GS->GetRoundState() == EDMRoundState::Success)
		{
			Check(true, TEXT("target_reached_succeeds"), FString::Printf(TEXT("collected %d of %d"), GS->GetCollectedValue(), GS->GetTargetValue()));
			return true;
		}
		return false;
	});

	AddWaitForRestart(1);
}

void ADMProcSelfTest::AddRoundTwoSteps()
{
	AddWaitForRound();
	Add(TEXT("restart_same_seed_same_layout"), 1.f, [this](float)
	{
		Check(true, TEXT("restart_after_success"), TEXT("a new round started after the success summary"));
		const APLGGenerator* Gen = Generator();
		int32 Forced = 0;
		const bool bFixed = FParse::Value(FCommandLine::Get(), TEXT("plgseed="), Forced) && Forced != 0;
		if (bFixed && Gen)
		{
			Check(Gen->GetSeed() == GRun.FirstSeed && Gen->GetLayoutHash() == GRun.FirstHash, TEXT("same_seed_same_layout"),
				FString::Printf(TEXT("seed %d/%d, hash %08x/%08x"), Gen->GetSeed(), GRun.FirstSeed, Gen->GetLayoutHash(), GRun.FirstHash));
		}
		else
		{
			Info(TEXT("no -plgseed: skipping the same-layout check"));
		}
		SetMonstersActive(false);
		return true;
	});
	Add(TEXT("timeout_fails"), 5.f, [this](float Age)
	{
		ADMGameState* GS = State();
		if (!GS)
		{
			return false;
		}
		if (Age <= KINDA_SMALL_NUMBER)
		{
			GS->DebugDrainStability(GS->GetRemainingSeconds() - 1.f);
			return false;
		}
		if (GS->GetRoundState() == EDMRoundState::Failed)
		{
			Check(Doll() != nullptr, TEXT("timeout_fails"), FString::Printf(TEXT("failed %.1f s after stability ran out, doll still up"), Age - 1.f));
			return true;
		}
		return false;
	});
	AddWaitForRestart(2);
}

void ADMProcSelfTest::AddRoundThreeSteps()
{
	AddWaitForRound();
	Add(TEXT("solo_caught"), 4.f, [this](float Age)
	{
		ADMCharacter* D = Doll();
		ADMButler* B = Butler();
		APLGGenerator* Gen = Generator();
		if (Age <= KINDA_SMALL_NUMBER)
		{
			Check(true, TEXT("restart_after_failure"), TEXT("a new round started after the failure summary"));
			if (!D || !B || !Gen)
			{
				Check(false, TEXT("solo_caught"), TEXT("missing doll or butler"));
				return true;
			}
			SetMonstersActive(false);
			const FTransform Start = Gen->GetModuleTransform(0);
			PlaceDoll(Start.TransformPosition(FVector(300.f, -300.f, 0.f)) + FVector(0.f, 0.f, DollStandZ), Start.GetLocation());
			PlaceMonster(B, Start.TransformPosition(FVector(220.f, -300.f, 0.f)) + FVector(0.f, 0.f, ButlerZ), Start.TransformPosition(FVector(300.f, -300.f, 0.f)));
			B->SetActorTickEnabled(true);
			return false;
		}
		if (D)
		{
			return false;
		}
		Check(TActorIterator<ADMCottonBall>(GetWorld()) ? true : false, TEXT("butler_catches_unshielded_doll"), FString::Printf(TEXT("caught after %.1f s"), Age));
		return true;
	});
	Add(TEXT("solo_down_fails_at_once"), 3.f, [this](float Age)
	{
		const ADMGameState* GS = State();
		if (GS && GS->GetRoundState() == EDMRoundState::Failed)
		{
			Check(true, TEXT("solo_down_fails_at_once"), FString::Printf(TEXT("round failed %.1f s after the catch, no revive in normal play"), Age));
			return true;
		}
		return false;
	});
}
