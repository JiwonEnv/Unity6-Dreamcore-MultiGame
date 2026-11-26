#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "Math/RandomStream.h"
#include "PLGTypes.h"
#include "PLGGenerator.generated.h"

class APLGGenerator;
class UInstancedStaticMeshComponent;
class UNavigationSystemV1;
class UMaterialInterface;
class UStaticMesh;
class UTextRenderComponent;

/** bSucceeded is false when no seed produced a valid layout; nothing was built then. */
DECLARE_DYNAMIC_MULTICAST_DELEGATE_TwoParams(FPLGOnGenerated, APLGGenerator*, Generator, bool, bSucceeded);

/**
 * Drop one in a level: on BeginPlay it assembles the module kit from a seed and builds graybox floors and walls.
 * The server picks the seed and replicates it; every machine builds the same layout locally, nothing else is sent.
 * In the editor, "Preview" and "Preview New Seed" build the same thing without playing (not saved with the level).
 */
UCLASS()
class PROCLEVELGEN_API APLGGenerator : public AActor
{
	GENERATED_BODY()

public:
	APLGGenerator();

	virtual void BeginPlay() override;
	virtual void GetLifetimeReplicatedProps(TArray<FLifetimeProperty>& OutLifetimeProps) const override;

	/** Builds the layout for the current seed. */
	UFUNCTION(CallInEditor, BlueprintCallable, Category = "Generation")
	void Preview();

	UFUNCTION(CallInEditor, Category = "Generation")
	void PreviewNewSeed();

	UFUNCTION(CallInEditor, Category = "Generation")
	void ClearPreview();

	const FPLGLayout& GetLayout() const { return Layout; }
	/** The last generation produced a layout that passed PLG::ValidateLayout and was built. */
	bool IsGenerated() const { return bGenerationSucceeded; }
	uint32 GetLayoutHash() const;

	/**
	 * Server: the navmesh covers the built layout. Nav data exists, no build is pending, and every module has navmesh
	 * near its pivot (within a quarter of its size, inside its own footprint). Says what is missing otherwise.
	 */
	bool IsNavigationReady(FString& OutWhy) const;

	/**
	 * Checks the built layout and reports each result: generation succeeded, the layout is valid, graybox exists,
	 * the first player stands on generated floor, and navigation reaches every module from the start.
	 * A module with no navmesh near its pivot counts as not reached. Returns the number of failures.
	 */
	int32 RunGenerationChecks(TFunctionRef<void(bool /*bPassed*/, const FString& /*What*/)> Report) const;
	const TArray<FPLGModuleDef>& GetModules() const { return Modules; }
	const TArray<FPLGTheme>& GetThemes() const { return Themes; }
	int32 GetSeed() const { return Seed; }

	/** Every spawn point with this tag in the built layout, in world space. */
	UFUNCTION(BlueprintPure, Category = "Generation")
	TArray<FPLGSpawnSlot> GetSpawnSlots(FName Tag) const;

	/** Random stream for content choices, derived from the seed (the server uses it; clients get the results replicated). */
	FRandomStream MakeContentStream() const;

	/** World transform of a placed module's pivot (floor top, footprint center). */
	UFUNCTION(BlueprintPure, Category = "Generation")
	FTransform GetModuleTransform(int32 PlacedIndex) const;

	/** Fired on every machine after generation, successful or not. Content spawning hooks in here. */
	UPROPERTY(BlueprintAssignable, Category = "Generation")
	FPLGOnGenerated OnGenerated;

protected:
	/** 0 or bNewSeedEachRun: the server picks one when play starts. */
	UPROPERTY(EditAnywhere, ReplicatedUsing = OnRep_Seed, Category = "Generation")
	int32 Seed = 1;

	UPROPERTY(EditAnywhere, Category = "Generation")
	bool bNewSeedEachRun = true;

	/** General rooms placed before the end room. */
	UPROPERTY(EditAnywhere, Category = "Generation", meta = (ClampMin = "0", ClampMax = "32"))
	int32 RoomCount = 4;

	UPROPERTY(EditAnywhere, Category = "Generation", meta = (ClampMin = "0", ClampMax = "1"))
	float CorridorChance = 0.45f;

	/** Server: further seeds tried when a seed gives no valid layout (each seed already retries internally). */
	UPROPERTY(EditAnywhere, Category = "Generation", meta = (ClampMin = "0", ClampMax = "16"))
	int32 MaxSeedRetries = 3;

	/** Modules stay within this distance (cm) of the generator, so one nav bounds volume can cover the house. */
	UPROPERTY(EditAnywhere, Category = "Generation", meta = (ClampMin = "1000"))
	float MaxHalfExtent = 5000.f;

	UPROPERTY(EditAnywhere, Category = "Kit")
	TArray<FPLGModuleDef> Modules;

	UPROPERTY(EditAnywhere, Category = "Kit")
	TArray<FPLGTheme> Themes;

	/** Grid the kit is checked against (cm). */
	UPROPERTY(EditAnywhere, Category = "Kit|Spec")
	float GridSize = 50.f;

	UPROPERTY(EditAnywhere, Category = "Kit|Spec")
	float WallHeight = 400.f;

	/** Full wall thickness; each module builds half of it inside its footprint. */
	UPROPERTY(EditAnywhere, Category = "Kit|Spec")
	float WallThickness = 20.f;

	UPROPERTY(EditAnywhere, Category = "Kit|Spec")
	float DoorHeight = 250.f;

	/** Floor slab below Z = 0. */
	UPROPERTY(EditAnywhere, Category = "Kit|Spec")
	float FloorThickness = 20.f;

	UPROPERTY(EditAnywhere, Category = "Graybox")
	FLinearColor FixedFloorColor = FLinearColor(0.55f, 0.42f, 0.32f);

	UPROPERTY(EditAnywhere, Category = "Graybox")
	FLinearColor CorridorFloorColor = FLinearColor(0.4f, 0.36f, 0.33f);

	UPROPERTY(EditAnywhere, Category = "Graybox")
	FLinearColor DefaultWallColor = FLinearColor(0.9f, 0.85f, 0.75f);

	UPROPERTY(EditAnywhere, Category = "Graybox")
	FLinearColor PropColor = FLinearColor(0.35f, 0.22f, 0.15f);

	/** Module name (and theme) floating in each module. */
	UPROPERTY(EditAnywhere, Category = "Graybox")
	bool bShowLabels = true;

	UPROPERTY(VisibleInstanceOnly, Transient, Category = "Generation")
	FPLGLayout Layout;

private:
	UFUNCTION()
	void OnRep_Seed();

	/** Builds the layout for Seed. With bAllowReseed (server), tries up to MaxSeedRetries more seeds and keeps the one that worked. */
	void Generate(bool bAllowReseed);

	UPROPERTY(VisibleInstanceOnly, Transient, Category = "Generation")
	bool bGenerationSucceeded = false;
	void BuildGraybox();
	void AddModuleGeometry(const FPLGPlacedModule& Placed, const FTransform& ModuleToWorld);

	/** Navmesh point near a module's pivot and inside its footprint (see IsNavigationReady). */
	bool ProjectIntoModule(UNavigationSystemV1& Nav, int32 PlacedIndex, FVector& OutPoint) const;

	/** Graybox mesh for one color, created on demand. */
	UInstancedStaticMeshComponent* GetBoxes(const FLinearColor& Color);

#if !UE_BUILD_SHIPPING
	/**
	 * -plgselftest: after the runtime navmesh settles, checks the layout, that the first player stands on the
	 * generated floor and that navigation reaches every module from the start, takes a player and a top-down
	 * screenshot, then quits. Results are [PLGSelfTest] PASS/FAIL lines in the log.
	 */
	void RunSelfTestChecks();
	void TakeTopDownShot();
	void SelfTestCheck(bool bPassed, const FString& What);
	int32 SelfTestFailures = 0;
#endif

	UPROPERTY(Transient)
	TObjectPtr<UStaticMesh> CubeMesh;

	UPROPERTY(Transient)
	TObjectPtr<UMaterialInterface> ShapeMaterial;

	/** One instanced mesh per graybox color; BoxColors[i] is the color of Boxes[i]. */
	UPROPERTY(Transient)
	TArray<FLinearColor> BoxColors;

	UPROPERTY(Transient)
	TArray<TObjectPtr<UInstancedStaticMeshComponent>> Boxes;

	UPROPERTY(Transient)
	TArray<TObjectPtr<UTextRenderComponent>> Labels;
};
