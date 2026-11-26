#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "DMGameMode.generated.h"

class APLGGenerator;

/**
 * One dream run: the target is a share of everything on the map that grows with the player count,
 * the timer is the dream's stability. Banking the target wins, running out of stability fails; the round restarts on its own after a short summary.
 */
UCLASS()
class DREAMMOVERS_API ADMGameMode : public AGameModeBase
{
	GENERATED_BODY()

public:
	ADMGameMode();

	virtual void StartPlay() override;
	virtual void PostLogin(APlayerController* NewPlayer) override;
	virtual void Logout(AController* Exiting) override;
	virtual bool PlayerCanRestart_Implementation(APlayerController* Player) override;

	/** A cotton ball reached the extraction zone: give its player a fresh doll there. */
	void RevivePlayer(AController* Controller, const FVector& Location);

	/** Why the round was aborted (empty unless it was). */
	const FString& GetAbortReason() const { return AbortReason; }
	int32 GetMapValue() const { return MapValue; }
	/** Generated maps: antiques that should be in the map, and how many spawn spots were already blocked. */
	int32 GetExpectedAntiques() const { return ExpectedAntiques; }
	int32 GetBlockedAntiqueSpawns() const { return BlockedAntiqueSpawns; }

protected:
	UPROPERTY(EditDefaultsOnly, Category = "Round", meta = (ClampMin = "10"))
	float RoundSeconds = 720.f;

	/** Target = map value x (BaseTargetShare + TargetSharePerPlayer x players): 30% solo, 60% for four. */
	UPROPERTY(EditDefaultsOnly, Category = "Round", meta = (ClampMin = "0", ClampMax = "1"))
	float BaseTargetShare = 0.2f;

	UPROPERTY(EditDefaultsOnly, Category = "Round", meta = (ClampMin = "0", ClampMax = "1"))
	float TargetSharePerPlayer = 0.1f;

	UPROPERTY(EditDefaultsOnly, Category = "Round", meta = (ClampMin = "1"))
	float SummarySeconds = 8.f;

	/** A generated map whose navmesh is not ready after this long aborts the round instead of starting it. */
	UPROPERTY(EditDefaultsOnly, Category = "Round", meta = (ClampMin = "1"))
	float NavReadyTimeoutSeconds = 30.f;

private:
	/** A generated map comes empty: wardrobes and antiques go on its spawn points before the target is counted. */
	void PopulateGeneratedLevel(APLGGenerator& Generator);
	/** Monsters only once the navmesh covers the house, so their first moves do not fail. */
	void SpawnGeneratedMonsters(APLGGenerator& Generator);
	/** Polls the generated map's navmesh; starts the round when ready, aborts on timeout. */
	void CheckMapReady();
	void BeginRound();
	void AbortRound(const FString& Reason);
	void UpdateTarget(const AController* Leaving = nullptr);
	void CheckRound();
	bool AreAllPlayersDown() const;
	void RestartRound();

	/** Value of every antique placed on the map when the round started. */
	int32 MapValue = 0;

	int32 ExpectedAntiques = 0;
	int32 BlockedAntiqueSpawns = 0;
	FString AbortReason;
	double PrepareStartTime = 0.0;
	/** The navmesh has to look ready on two polls in a row, so a build that is about to start is not mistaken for done. */
	int32 NavReadyPolls = 0;
	TWeakObjectPtr<APLGGenerator> GeneratedLevel;
	FTimerHandle ReadyTimer;

	FTimerHandle CheckTimer;
	FTimerHandle RestartTimer;
};
