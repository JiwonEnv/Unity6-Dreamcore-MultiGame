#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "DMProcSelfTest.generated.h"

/** Butler state as seen by the test (mirrors EDMMonsterState without pulling in DMMonster.h). */
enum class EDMMonsterStateMirror : uint8
{
	Idle, Patrol, Chase, Charge, Search, Return, Unknown
};

class ADMBellHorse;
class ADMButler;
class ADMCharacter;
class ADMGameMode;
class ADMGameState;
class ADMHidingSpot;
class ADMValuable;
class APLGGenerator;

/**
 * Development-only single-player test of a generated map, started with `-dmproctest` on Lvl_ProcGen (never in Shipping).
 * Three rounds in one process: round 1 checks readiness, the layout, content, the wardrobe, monsters, catching,
 * crouching, grabbing, delivery, stability and Collapse, then reaches the target; round 2 runs out of stability;
 * round 3 gets the solo doll caught. `-dmprocexpect=abort` instead checks that a broken map never starts a round.
 * Use `-plgseed=N` so the restarted rounds can be compared with the first.
 * Logs "[DMTest] PASS/FAIL <check>: <detail>" lines and quits with exit code 0 only if every check passed.
 */
UCLASS(NotPlaceable)
class DREAMMOVERS_API ADMProcSelfTest : public AActor
{
	GENERATED_BODY()

public:
	ADMProcSelfTest();

	virtual void BeginPlay() override;
	virtual void EndPlay(const EEndPlayReason::Type EndPlayReason) override;
	virtual void Tick(float DeltaSeconds) override;

private:
	/** Run is called every tick with the step's age in seconds and returns true when the step is done. */
	struct FStep
	{
		FString Name;
		float Timeout = 10.f;
		/** A timed-out fatal step ends the whole test. */
		bool bFatal = false;
		TFunction<bool(float)> Run;
	};

	void Add(const TCHAR* Name, float Timeout, TFunction<bool(float)> Run, bool bFatal = false);
	void Check(bool bPassed, const FString& Name, const FString& Detail = FString());
	void Info(const FString& Text);
	void Finish();

	void AddWaitForRound();
	void AddRoundOneSteps();
	void AddRoundTwoSteps();
	void AddRoundThreeSteps();
	void AddAbortSteps();
	/** -dmprocexpect=deskhide: spotted for real, crawl under a desk, the butler gives up and patrols, crawl out, spotted again. */
	void AddDeskHideSteps();
	/**
	 * bBlocked: a low barrier across the room keeps the butler about 3 m away (it can look over it, not walk through).
	 * bExposed: the doll's body sticks out past the desk edge; being caught or chased is the expected outcome.
	 */
	void AddDeskHideCase(const TCHAR* Name, const FVector2D& ApproachDir, const FVector2D& HideOffset, bool bBlocked = false, bool bExposed = false);
	/** Standing in the open inside a 60 cm fence: in plain view, out of reach. The butler must give up, not wait forever. */
	void AddFencedCase();
	/** Ends this round's steps; the game mode restarts the map and the next round's test actor carries on. */
	void AddWaitForRestart(int32 NextRound);

	ADMCharacter* Doll() const;
	ADMGameState* State() const;
	ADMGameMode* Mode() const;
	APLGGenerator* Generator() const;
	ADMButler* Butler() const;
	ADMBellHorse* Horse() const;

	void SetMonstersActive(bool bActive);
	/** Sends the horse back to its post and stops it, so a noisy check does not leave it in the way. */
	void ParkHorse();
	float FloorZ() const;
	/** Exact placement (no encroachment fix-up), facing LookAt. */
	bool PlaceDoll(const FVector& Location, const FVector& LookAt);
	bool PlaceMonster(AActor* Monster, const FVector& Location, const FVector& LookAt);
	void AimAt(const FVector& Point);
	void TeleportItem(ADMValuable* Item, const FVector& Location);
	/** A free floor spot DistanceMin..DistanceMax from the item with a clear view of it, for a standing doll. */
	bool FindStandSpot(const ADMValuable* Item, FVector& OutSpot) const;
	bool IsInsideHouse(const FVector& WorldPoint) const;
	void OnNoise(const FVector& Location, float Radius, AActor* Source);

	TArray<FStep> Steps;
	int32 Current = 0;
	float StepAge = 0.f;
	bool bFinished = false;
	int32 Round = 0;

	FDelegateHandle NoiseHandle;
	int32 NoisesFromDoll = 0;
	int32 NoisesFromWardrobe = 0;

	// State shared between steps of one round.
	bool bSawPreparing = false;
	bool bMonstersBeforeReady = false;
	bool bTimerBeforeReady = false;
	TWeakObjectPtr<ADMHidingSpot> Wardrobe;
	TWeakObjectPtr<AActor> Blocker;
	TArray<TWeakObjectPtr<AActor>> Fence;
	FVector HiddenAt = FVector::ZeroVector;
	TSet<int32> ButlerVisited;
	FVector ButlerStart = FVector::ZeroVector;
	float ButlerTravel = 0.f;
	FVector LastButlerLocation = FVector::ZeroVector;
	FVector NoiseAt = FVector::ZeroVector;
	bool bNoiseSent = false;
	bool bHorseCharged = false;
	TWeakObjectPtr<ADMValuable> Light;
	TWeakObjectPtr<ADMValuable> Heavy;
	TWeakObjectPtr<ADMValuable> Spare;
	float ItemStartZ = 0.f;
	TArray<FVector> CarryPath;
	int32 CarryIndex = 0;
	int32 DeliverValue = 0;
	int32 CollectedBefore = 0;
	double RemainingBefore = 0.0;
	double DeliverTime = 0.0;

	// Desk hide cases (module space of the desk's room).
	int32 DeskRoom = INDEX_NONE;
	int32 CasePhase = 0;
	int32 CaseRetries = 0;
	double PhaseStart = 0.0;
	FVector CaseHide = FVector::ZeroVector;
	FVector CaseOut = FVector::ZeroVector;
	FVector ButlerAtPatrol = FVector::ZeroVector;
	FString CaseLog;
	EDMMonsterStateMirror LastButlerState = EDMMonsterStateMirror::Unknown;
};
