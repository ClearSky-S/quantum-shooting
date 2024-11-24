

#region quantum_unity/Assets/Photon/Quantum/Editor/AssetPipeline/AssetBasePostprocessor.cs
namespace Quantum.Editor {
  using System;
  using System.Linq;
  using UnityEditor;
  using UnityEngine;
#if UNITY_2021_2_OR_NEWER
  using UnityEditor.SceneManagement;
#else
  using UnityEditor.Experimental.SceneManagement;
#endif

  public class AssetBasePostprocessor : AssetPostprocessor {

    private static int? _removeMonoBehaviourUndoGroup;
    private static int _reentryCount = 0;
    private const int MaxReentryCount = 3;

    [Flags]
    private enum ValidationResult {
      Ok,
      Dirty = 1,
      Invalidated = 2,
    }
    
#if !UNITY_2021_1_OR_NEWER
    [InitializeOnLoadMethod]
    static void SetupVariantPrefabWorkarounds() {
      PrefabStage.prefabSaving += OnPrefabStageSaving;
      PrefabStage.prefabStageClosing += OnPrefabStageClosing;
    }

    static void OnPrefabStageClosing(PrefabStage stage) {

      var assetPath = stage.GetAssetPath();
      var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
      if (PrefabUtility.GetPrefabAssetType(prefabAsset) != PrefabAssetType.Variant)
        return;

      // restore references
      ValidateQuantumAsset(assetPath, ignoreVariantPrefabWorkaround: true);
    }

    static void OnPrefabStageSaving(GameObject obj) {
      var stage = PrefabStageUtility.GetCurrentPrefabStage();
      if (stage == null)
        return;

      var assetPath = stage.GetAssetPath();
      var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
      if (PrefabUtility.GetPrefabAssetType(prefabAsset) != PrefabAssetType.Variant)
        return;

      // nested assets of variant prefabs holding component references raise internal Unity error;
      // these references need to be cleared before first save
      var nestedAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AssetBase>();
      foreach (var nestedAsset in nestedAssets) {
        if (nestedAsset is IQuantumPrefabNestedAsset == false)
          continue;
        NestedAssetBaseEditor.ClearParent(nestedAsset);
      }
    }
#endif

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths) {
      try {

        if (++_reentryCount > MaxReentryCount) {
          Debug.LogError("Exceeded max reentry count, possibly a bug");
          return;
        }

        if (QuantumEditorSettings.InstanceFailSilently?.UseAssetBasePostprocessor == true) {

#if UNITY_2020_1_OR_NEWER
          // this is a workaround for EditorUtility.SetDirty and AssetDatabase.SaveAssets not working nicely
          // with postprocessors; a dummy FindAssets call prior to SaveAssets seems to flush internal state and fix the issue
          AssetDatabase.FindAssets($"t:AssetThatDoesNotExist", QuantumEditorSettings.Instance.AssetSearchPaths);
#endif

          var result = ValidationResult.Ok;

          foreach (var importedAsset in importedAssets) {
            result |= ValidateQuantumAsset(importedAsset);
          }

          foreach (var movedAsset in movedAssets) {
            result |= ValidateQuantumAsset(movedAsset);
          }

          for (int i = 0; result == ValidationResult.Ok && i < deletedAssets.Length; i++) {
            if (QuantumEditorSettings.Instance.AssetSearchPaths.Any(p => deletedAssets[i].StartsWith(p))) {
              result |= ValidationResult.Invalidated;
            }
          }

#if !UNITY_2020_1_OR_NEWER
          if (result.HasFlag(ValidationResult.Dirty)) {
            AssetDatabase.SaveAssets();
          }
#endif

          if (result.HasFlag(ValidationResult.Invalidated) || result.HasFlag(ValidationResult.Dirty)) {
            AssetDBGeneration.OnAssetDBInvalidated?.Invoke();
          }

#if UNITY_2020_1_OR_NEWER
          if (result.HasFlag(ValidationResult.Dirty)) {
            AssetDatabase.SaveAssets();
          }
#endif
        }
      } finally {
        --_reentryCount;
      }
    }

    private static ValidationResult ValidateQuantumAsset(string path, bool ignoreVariantPrefabWorkaround = false) {

      var result = ValidationResult.Ok;

      for (int i = 0; i < QuantumEditorSettings.Instance.AssetSearchPaths.Length; i++) {
        if (path.StartsWith(QuantumEditorSettings.Instance.AssetSearchPaths[i]) && !path.EndsWith(".unity")) {
          var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);

#if UNITY_2020_3_OR_NEWER
          var nestedAssets = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AssetBase>()
            .Where(x => x != mainAsset);
#endif

          if (mainAsset is AssetBase asset) {
            result |= ValidateAsset(asset, path);
          } else if (mainAsset is GameObject prefab) {

            if (!ignoreVariantPrefabWorkaround) {
              // there is some weirdness in how Unity handles variant prefabs; basically you can't reference any components
              // externally in that stage, or you'll get an internal error
              if (PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant && PrefabStageUtility.GetCurrentPrefabStage()?.GetAssetPath() == path) {
                break;
              }
            }

            result |= ValidatePrefab(prefab, path);
          }
          else if (mainAsset is SceneAsset) {
            continue;
          }

#if !UNITY_2020_3_OR_NEWER
          var nestedAssets = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AssetBase>()
            .Where(x => x != mainAsset);
#endif

          foreach (var nestedAsset in nestedAssets) {
            if (!nestedAsset) {
              // destroyed while the main asset was validated; it is up to the main
              // asset's validation to set the result flags
            } else {
              result |= ValidateAsset(nestedAsset, path);
            }
          }
        }
      }

      return result;
    }

    private static ValidationResult ValidateAsset(AssetBase asset, string assetPath) {
      Debug.Assert(!string.IsNullOrEmpty(assetPath));

      if (asset.IsTransient) {
        // fully transient
        return ValidationResult.Ok;
      }

      var correctPath = asset.GenerateDefaultPath(assetPath);

      ValidationResult result = ValidationResult.Ok;

      if (string.IsNullOrEmpty(asset.AssetObject.Path)) {
        asset.AssetObject.Path = correctPath;
        result |= ValidationResult.Dirty;
      } else {
        if (!asset.AssetObject.Path.Equals(correctPath)) {
          // possible duplication
          var sourceAssetPath = asset.AssetObject.Path;

          // ditch everything after the separator 
          var separatorIndex = sourceAssetPath.LastIndexOf(AssetBase.NestedPathSeparator);
          if (separatorIndex >= 0) {
            sourceAssetPath = sourceAssetPath.Substring(0, separatorIndex);
          }

          var wasCloned = AssetDatabase.LoadAllAssetsAtPath($"Assets/{sourceAssetPath}.asset")
            .Concat(AssetDatabase.LoadAllAssetsAtPath($"Assets/{sourceAssetPath}.prefab"))
            .OfType<AssetBase>()
            .Any(x => x.AssetObject?.Guid == asset.AssetObject.Guid);

          if (wasCloned) {
            var newGuid = AssetGuid.NewGuid();
            Debug.LogFormat(asset, "Asset {0} ({3}) appears to have been cloned, assigning new id: {1} (old id: {2})", assetPath, newGuid, asset.AssetObject.Guid, asset.GetType());
            asset.AssetObject.Guid = newGuid;
            result |= ValidationResult.Invalidated;
          }

          asset.AssetObject.Path = correctPath;
          result |= ValidationResult.Dirty;
        }
      }

      if (!asset.AssetObject.Guid.IsValid) {
        asset.AssetObject.Guid = AssetDBGeneration.TryGetDefaultAssetGuid(assetPath, out var defaultGuid) ? defaultGuid : AssetGuid.NewGuid();
        result |= ValidationResult.Dirty;
        result |= ValidationResult.Invalidated;
      }

      if (result.HasFlag(ValidationResult.Dirty)) {
        EditorUtility.SetDirty(asset);
      }

      return result;
    }

    private static ValidationResult ValidatePrefab(GameObject prefab, string prefabPath) {
      Debug.Assert(!string.IsNullOrEmpty(prefabPath));
      var result = ValidationResult.Ok;

      var existingNestedAssets = AssetDatabase.LoadAllAssetsAtPath(prefabPath).OfType<IQuantumPrefabNestedAsset>().ToList();

      foreach (var component in prefab.GetComponents<MonoBehaviour>()) {
        if (component == null) {
          Debug.LogWarning($"Asset {prefab} has a missing component", prefab);
          continue;
        }

        if ( component is IQuantumPrefabNestedAssetHost host ) {
          var nestedAssetType = host.NestedAssetType;

          if (nestedAssetType == null || nestedAssetType.IsAbstract) {
            Debug.LogError($"{component.GetType().FullName} component's NestedAssetType property is either null or abstract, unable to create.", prefab);
            continue;
          }

          if (NestedAssetBaseEditor.EnsureExists(component, nestedAssetType, out var nested, save: false)) {
            // saving will trigger the postprocessor again
            result |= ValidationResult.Dirty;
          }

          existingNestedAssets.Remove(nested);
        }
      }

      foreach (var orphaned in existingNestedAssets) {
        var obj = (AssetBase)orphaned;
        Debug.LogFormat("Deleting orphaned nested asset: {0} (in {1})", obj, prefabPath);
        if (Undo.GetCurrentGroupName() == "Remove MonoBehaviour" || _removeMonoBehaviourUndoGroup == Undo.GetCurrentGroup()) {
          // special case: when component gets removed with context menu, we want to be able to restore
          // asset with the original guid
          _removeMonoBehaviourUndoGroup = Undo.GetCurrentGroup();
          Undo.DestroyObjectImmediate(obj);
        } else {
          _removeMonoBehaviourUndoGroup = null;
          UnityEngine.Object.DestroyImmediate(obj, true);
        }
        result |= ValidationResult.Dirty;
      }

      if (result.HasFlag(ValidationResult.Dirty)) {
        EditorUtility.SetDirty(prefab);
      }

      return result;
    }
  }

  static class PrefabStageExtensions {
    public static string GetAssetPath(this PrefabStage stage) {
#if UNITY_2020_1_OR_NEWER
      return stage.assetPath;
#else
      return stage.prefabAssetPath;
#endif
    }
  }
}

#endregion

#region quantum_unity/Assets/Photon/Quantum/Editor/AssetPipeline/AssetDBGeneration.cs
//#define QUANTUM_ENABLE_ASSETDBGENERATION_TRACE
namespace Quantum.Editor {
  using System;
  using UnityEngine;
  using UnityEditor;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Reflection;
  
  public static partial class AssetDBGeneration {
    
    // Reset this when other behavior than an immediate AssetDB is desired after creating new or changing Quantum asset guids.
    public static Action OnAssetDBInvalidated                    = DefaultOnAssetDBInvalidated;
    public static int    GenerateAssetDBIterationMaxMilliseconds = 20;
    
    private static IEnumerator<object> _currentCoroutine;
    
    public static void DefaultOnAssetDBInvalidated() {
      if (QuantumEditorSettings.InstanceFailSilently?.UseAsyncAssetDBGeneration == true) {
        GenerateAsync();  
      } else {
        Generate();
      }
    }
    
    [MenuItem("Quantum/Generate Asset Resources", true, 21)]
    [MenuItem("Quantum/Generate Asset Resources (Async)", true, 22)]
    public static bool GenerateValidation() {
      return !Application.isPlaying;
    }

    [MenuItem("Quantum/Generate Asset Resources", false, 21)]
    public static void Generate() {
      StopAndClearCoroutine();
      
      if (Application.isPlaying) {
        return;
      }
      
      LogTrace("Generating synchronously");
      using (var enumerator = GenerateAssetDB(int.MaxValue)) {
        bool hasMore = enumerator.MoveNext();
        Debug.Assert(!hasMore);
      }
    }
    
    [MenuItem("Quantum/Generate Asset Resources (Async)", false, 22)]
    public static void GenerateAsync() {
      StopAndClearCoroutine();
      
      if (Application.isPlaying) {
        return;
      }

      LogTrace("Generating asynchronously");
      _currentCoroutine = GenerateAssetDB(GenerateAssetDBIterationMaxMilliseconds);
      Debug.Assert(_currentCoroutine != null);
      EditorApplication.playModeStateChanged += OnPlayModeChanged;
      EditorApplication.update += UpdateCoroutine;
    }
    
    static IEnumerator<object> GenerateAssetDB(int iterationMaxMilliseconds = 20) {
      
      var  allAssets  = new SortedList<AssetGuid, AssetBase>();

      var timer  = CoroutineTimer.StartNew(iterationMaxMilliseconds);
      var isDone = false;
      
      try {
        // phase 1 - finding all Asset wrappers
        string[] assetGuids;
        {
          assetGuids = AssetDatabase.FindAssets($"t:{nameof(AssetBase)}", QuantumEditorSettings.Instance.AssetSearchPaths);
          if (timer.TryBeginYield()) {
            yield return null;
            timer.EndYield();
          }

          LogTrace("Finished discovering all AssetBase assets at {0}ms", timer.ElapsedMilliseconds);
        }

        // phase 2 - loading all Asset wrappers
        {
          var pathMap = new Dictionary<string, AssetBase>();
          foreach (var assetGuid in assetGuids.Distinct()) {
            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AssetBase>()) {
              if (!asset) {
                continue;
              }

              if (asset is IQuantumPrefabNestedAsset nested) {
                // check if this asset is split
                if (!asset.IsTransient && NestedAssetBaseEditor.HasPrefabAsset(nested)) {
                  continue;
                }
              }

              EnsureAssetGuidAndPathAreValid(asset);

              if (allAssets.TryGetValue(asset.AssetObject.Guid, out var conflicting)) {
                Debug.LogError($"Duplicated guid {asset.AssetObject.Guid} found and skipping asset at path '{asset.AssetObject.Path}'. Conflicting asset: {AssetDatabase.GetAssetPath(conflicting)}", asset);
                continue;
              }

              if (pathMap.TryGetValue(asset.AssetObject.Path, out conflicting)) {
                Debug.LogError($"Duplicated path '{asset.AssetObject.Path}' found and skipping asset with guid {asset.AssetObject.Guid}. Conflicting asset: {AssetDatabase.GetAssetPath(conflicting)}", asset);
                continue;
              }

              pathMap.Add(asset.AssetObject.Path, asset);
              allAssets.Add(asset.AssetObject.Guid, asset);

              if (timer.TryBeginYield()) {
                yield return null;
                timer.EndYield();
              }
            }
          }

          LogTrace("Finished loading all AssetBase assets at {0}ms", timer.ElapsedMilliseconds);
        }

        // phase 3 - refresh AssetResourceContainer
        AssetResourceContainer container;
        {
          // Overwrite the resource container and add found assets
          container = AssetDatabase.LoadAssetAtPath<AssetResourceContainer>(QuantumEditorSettings.Instance.AssetResourcesPath);
          if (container == null) {
            container = ScriptableObject.CreateInstance<AssetResourceContainer>();
            AssetDatabase.CreateAsset(container, QuantumEditorSettings.Instance.AssetResourcesPath);
          }

          // initialize the container
          container.EditorGuidToPathCache.Clear();

          var createResourceInfoDelegates = new List<Delegate>();
          foreach (var group in container.Groups) {
            group.Clear();

            var generatorType = typeof(Func<,,>).MakeGenericType(group.GetType(), typeof(AssetContext), typeof(AssetResourceInfo));
            createResourceInfoDelegates.Add(typeof(AssetDBGeneration).CreateMethodDelegate(nameof(TryCreateResourceInfo), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, generatorType));
          }

          foreach (var kv in allAssets) {
            var asset = kv.Value;
            if (!asset) {
              continue;
            }

            container.EditorGuidToPathCache.Add(kv.Key, asset.AssetObject.Path);
          }

          if (timer.TryBeginYield()) {
            yield return null;
            timer.EndYield();
          }

          var context = new AssetContext();
          foreach (var kv in allAssets) {
            var asset = kv.Value;
            if (!asset) {
              continue;
            }

            context.Asset = asset;

            bool found = false;
            for (int i = 0; i < container.Groups.Count; ++i) {
              var group = container.Groups[i];
              var info  = (AssetResourceInfo)createResourceInfoDelegates[i].DynamicInvoke(group, context);

              if (info != null) {
                info.Guid = asset.AssetObject.Guid;
                info.Path = asset.AssetObject.Path;
                group.Add(info);
                found = true;
                break;
              }
            }

            if (!found) {
              Debug.LogError($"Failed to find a resource group for {asset.AssetObject.Identifier}. " +
                             $"Make sure this asset is either in Resources, has an AssetBundle assigned, is an Addressable (if QUANTUM_ENABLE_ADDRESSABLES is defined) " +
                             $"or add your own custom group to handle it.", asset);
              continue;
            }

            if (timer.TryBeginYield()) {
              yield return null;
              timer.EndYield();
            }
          }

          LogTrace("Finished updating AssetResourceContainer at {0}ms", timer.ElapsedMilliseconds);
        }

        // phase 4 - clean up
        {
          EditorUtility.SetDirty(container);
          UnityDB.Dispose();
          isDone = true;
          
          LogTrace("Finished at {0}ms", timer.ElapsedMilliseconds);
          Debug.Log("Rebuild Quantum Asset DB");
        }
      } finally {
        if (!isDone) {
          LogTrace("Interrupted at {0}ms", timer.ElapsedMilliseconds);
        }
      }
    }
    
    public static List<AssetBase> GatherQuantumAssets() {
      var allAssets = new List<AssetBase>();

      var assetGuids = AssetDatabase.FindAssets($"t:{nameof(AssetBase)}", QuantumEditorSettings.Instance.AssetSearchPaths);
      foreach (var assetGuid in assetGuids.Distinct()) {
        foreach (var assetBase in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(assetGuid)).OfType<AssetBase>()) {
          if (assetBase is IQuantumPrefabNestedAsset nested) {
            // check if this asset is split
            if (!assetBase.IsTransient && NestedAssetBaseEditor.HasPrefabAsset(nested)) {
              continue;
            }
          }

          allAssets.Add(assetBase);
        }
      }

      foreach (var assetBase in allAssets) {
        EnsureAssetGuidAndPathAreValid(assetBase);
      }

      allAssets.Sort((a, b) => a.AssetObject.Guid.CompareTo(b.AssetObject.Guid));

      return allAssets;
    }

    public static bool TryGetDefaultAssetGuid(string assetPath, out AssetGuid guid) {
      var assetFileName = Path.GetFileName(assetPath);

      // Recover default config settings
      switch (assetFileName) {
        case "DefaultCharacterController2D.asset":
          guid = (long)DefaultAssetGuids.CharacterController2DConfig;
          break;
        case "DefaultCharacterController3D.asset":
          guid = (long)DefaultAssetGuids.CharacterController3DConfig;
          break;
        case "DefaultNavMeshAgentConfig.asset":
          guid = (long)DefaultAssetGuids.NavMeshAgentConfig;
          break;
        case "DefaultPhysicsMaterial.asset":
          guid = (long)DefaultAssetGuids.PhysicsMaterial;
          break;
        case "SimulationConfig.asset":
          guid = (long)DefaultAssetGuids.SimulationConfig;
          break;
        default:
          guid = default;
          return false;
      }

      return true;
    }
    
    static AssetResourceInfo TryCreateResourceInfo(AssetResourceContainer.AssetResourceInfoGroup_Resources group, AssetContext context) {
      if (PathUtils.MakeRelativeToFolder(context.UnityAssetPath, "Resources", out var resourcePath)) {
        // drop the extension
        return new AssetResourceContainer.AssetResourceInfo_Resources() {
          ResourcePath = PathUtils.GetPathWithoutExtension(resourcePath)
        };
      }
      return null;
    }

    static AssetResourceInfo TryCreateResourceInfo(AssetResourceContainer.AssetResourceInfoGroup_AssetBundle group, AssetContext context) {
      var assetBundleName = AssetDatabase.GetImplicitAssetBundleName(context.UnityAssetPath);
      if (!string.IsNullOrEmpty(assetBundleName)) {
        return new AssetResourceContainer.AssetResourceInfo_AssetBundle() {
          AssetBundle = assetBundleName,
          AssetName = Path.GetFileName(context.UnityAssetPath),
        };
      }
      return null;
    }

    static void EnsureAssetGuidAndPathAreValid(AssetBase assetBase) {
      if (!assetBase || assetBase.IsTransient) {
        return;
      }

      var assetPath = AssetDatabase.GetAssetPath(assetBase);

      // Fix invalid guid ids.
      if (!assetBase.AssetObject.Guid.IsValid) {
        if (TryGetDefaultAssetGuid(assetPath, out var defaultGuid)) {
          assetBase.AssetObject.Guid = defaultGuid;
          Debug.LogWarning($"Set default guid {assetBase.AssetObject.Guid} for asset at path '{assetPath}'");
        } else {
          assetBase.AssetObject.Guid = AssetGuid.NewGuid();
          Debug.LogWarning($"Generated a new guid {assetBase.AssetObject.Guid} for asset at path '{assetPath}'");
        }

        EditorUtility.SetDirty(assetBase);
      }

      // Fix invalid paths
      var correctPath = assetBase.GenerateDefaultPath(assetPath);
      if (string.IsNullOrEmpty(assetBase.AssetObject.Path) || assetBase.AssetObject.Path != correctPath) {
        assetBase.AssetObject.Path = correctPath;

        Debug.LogWarning($"Generated a new path '{assetBase.AssetObject.Path}' for asset {assetBase.AssetObject.Guid}");

        if (string.IsNullOrEmpty(assetBase.AssetObject.Path)) {
          Debug.LogError($"Asset '{assetBase.AssetObject.Guid}' is not added to the Asset DB because it does not have a valid path");
          return;
        } else {
          EditorUtility.SetDirty(assetBase);
        }
      }
    }
    
    static void UpdateCoroutine() {
      Debug.Assert(_currentCoroutine != null);
      try {
        if (!_currentCoroutine.MoveNext()) {
          StopAndClearCoroutine();
        }
      } catch {
        StopAndClearCoroutine();
        throw;
      }
    }
    
    static void StopAndClearCoroutine() {
      if (_currentCoroutine == null) {
        return;
      }

      EditorApplication.playModeStateChanged -= OnPlayModeChanged;
      EditorApplication.update -= UpdateCoroutine;
      try {
        _currentCoroutine?.Dispose();
      } finally {
        _currentCoroutine = null;
      }
    }

    static void OnPlayModeChanged(PlayModeStateChange change) {
      Debug.Assert(_currentCoroutine != null);
      if (change == PlayModeStateChange.ExitingEditMode) {
        LogTrace("Play mode change detected, fast forwarding existing coroutine");
        EditorUtility.DisplayProgressBar("Finishing Asset DB Generation", "Finishing Asset DB Generation", -1);
        try {
          while (_currentCoroutine.MoveNext()) {
          }
        } finally {
          EditorUtility.ClearProgressBar();
          StopAndClearCoroutine();
        }
      }
    }
    
    [System.Diagnostics.Conditional("QUANTUM_ENABLE_ASSETDBGENERATION_TRACE")]
    static void LogTrace(string format, params object[] args) {
      Debug.LogFormat($"[AssetDBGeneration] " + format, args);
    }
    
    partial class AssetContext {
      public AssetBase Asset { get; set; }
      public bool IsMainAsset      => AssetDatabase.IsMainAsset(Asset);
      public string UnityAssetPath => AssetDatabase.GetAssetPath(Asset);
      public string UnityAssetGuid => AssetDatabase.AssetPathToGUID(UnityAssetPath);
      public string QuantumPath    => Asset.AssetObject.Path;
      public AssetGuid QuantumGuid => Asset.AssetObject.Guid;
    }
    
    struct CoroutineTimer {
      private System.Diagnostics.Stopwatch _stopwatch;
      private long                         _totalMills;
      private int                          _maxMills;

      public static CoroutineTimer StartNew(int maxMillisecondsPerIteration) {
        return new CoroutineTimer() {
          _stopwatch = System.Diagnostics.Stopwatch.StartNew(), 
          _maxMills = maxMillisecondsPerIteration,
        };
      }
      
      public bool TryBeginYield() {
        if (_stopwatch.ElapsedMilliseconds > _maxMills) {
          _totalMills += _stopwatch.ElapsedMilliseconds;
          return true;
        }
        return false;
      }
      
      public void EndYield() {
        _stopwatch.Restart();
      }
      
      public long ElapsedMilliseconds => _totalMills + _stopwatch.ElapsedMilliseconds;
    }
  }
}

#endregion

#region quantum_unity/Assets/Photon/Quantum/Editor/AssetPipeline/AssetDBGeneration_Addressables.cs
namespace Quantum.Editor {
#if (QUANTUM_ADDRESSABLES || QUANTUM_ENABLE_ADDRESSABLES) && !QUANTUM_DISABLE_ADDRESSABLES
  using System.Collections.Generic;
  using System.Linq;
  using UnityEditor.AddressableAssets;
  using UnityEditor.AddressableAssets.Settings;

  public static partial class AssetDBGeneration {


    private static AssetResourceInfo TryCreateResourceInfo(AssetResourceContainer.AssetResourceInfoGroup_Addressables group, AssetContext context) {
#if QUANTUM_ENABLE_ADDRESSABLES_FIND_ASSET_ENTRY
      var addressableEntry = AddressableAssetSettingsDefaultObject.Settings.FindAssetEntry(context.UnityAssetGuid, true);
#else
      var addressableEntry = context.GuidToParentAddressable[context.UnityAssetGuid].SingleOrDefault();
#endif
      if (addressableEntry != null) {
        string address = addressableEntry.address;
        if (!context.IsMainAsset) {
          address += $"[{context.Asset.name}]";
        }
        return new AssetResourceContainer.AssetResourceInfo_Addressables() {
          Address = address
        };
      }

      return null;
    }

#if !QUANTUM_ENABLE_ADDRESSABLES_FIND_ASSET_ENTRY
    partial class AssetContext {
      public ILookup<string, AddressableAssetEntry> GuidToParentAddressable = CreateAddressablesLookup();
    }
    
    public static ILookup<string, AddressableAssetEntry> CreateAddressablesLookup() {
      var assetList = new List<AddressableAssetEntry>();
      var assetsSettings = AddressableAssetSettingsDefaultObject.Settings;

      if (assetsSettings == null) {
        throw new System.InvalidOperationException("Unable to load Addressables settings. This may be due to an outdated Addressables version.");
      }

      foreach (var settingsGroup in assetsSettings.groups) {
        if (settingsGroup.ReadOnly)
          continue;
        settingsGroup.GatherAllAssets(assetList, true, true, true);
      }

      return assetList.Where(x => !string.IsNullOrEmpty(x.guid)).ToLookup(x => x.guid);
    }
#endif
  }
#endif
}
#endregion

#region quantum_unity/Assets/Photon/Quantum/Editor/AssetPipeline/AssetDBGeneration_Export.cs
namespace Quantum.Editor {

  using System.IO;
  using System.Linq;
  using UnityEditor;
  using UnityEngine;

  public static partial class AssetDBGeneration {
    private static string AssetDBLocation {
      get => EditorPrefs.GetString("Quantum_Export_LastDBLocation");
      set => EditorPrefs.SetString("Quantum_Export_LastDBLocation", value);
    }

    /// <summary>
    /// Discovers Quantum assets in the project, identically to the Asset DB Resource file, and export the data into JSON.
    /// </summary>
    [MenuItem("Quantum/Export/Asset DB %#t", false, 3)]
    public static void Export() {
      var lastLocation = AssetDBLocation;
      var directory = string.IsNullOrEmpty(lastLocation) ? Application.dataPath : Path.GetDirectoryName(lastLocation);
      var defaultName = string.IsNullOrEmpty(lastLocation) ? "db" : Path.GetFileName(lastLocation);

      var filePath = EditorUtility.SaveFilePanel("Save db to..", directory, defaultName, "json");
      if (!string.IsNullOrEmpty(filePath)) {
        Export(filePath);
        AssetDBLocation = filePath;
      }
    }

    public static void Export(string filePath) {
      var allAssets = GatherQuantumAssets();

      var allAssetObjects = allAssets.Select(a => {
        a.PrepareAsset();
        return a.AssetObject;
      });

      var serializer = new QuantumUnityJsonSerializer() { IsPrettyPrintEnabled = false };
      File.WriteAllBytes(filePath, serializer.SerializeAssets(allAssetObjects));
    }

    [MenuItem("Quantum/Export/Asset DB (Through UnityDB)", true, 3)]
    public static bool ExportThroughUnityDB_Validate() {
      return Application.isPlaying;
    }

    /// <summary>
    /// Use this when the asset loading has been customized by code that requires to start the game.
    /// </summary>
    [MenuItem("Quantum/Export/Asset DB (Through UnityDB)", false, 3)]
    public static void ExportThroughUnityDB() {
      var lastLocation = AssetDBLocation;
      var directory = string.IsNullOrEmpty(lastLocation) ? Application.dataPath : Path.GetDirectoryName(lastLocation);
      var defaultName = string.IsNullOrEmpty(lastLocation) ? "db" : Path.GetFileName(lastLocation);

      var filePath = EditorUtility.SaveFilePanel("Save db to..", directory, defaultName, "json");
      if (!string.IsNullOrEmpty(filePath)) {
        ExportThroughUnityDB(filePath);
        AssetDBLocation = filePath;
      }
    }

    public static void ExportThroughUnityDB(string filePath) {
      var serializer = new QuantumUnityJsonSerializer() { IsPrettyPrintEnabled = true };
      var assetObjectUnityDB = UnityDB.DefaultResourceManager.LoadAllAssets(true).ToList();
      assetObjectUnityDB.Sort((a, b) => a.Guid.CompareTo(b.Guid));
      File.WriteAllBytes(filePath, serializer.SerializeAssets(assetObjectUnityDB));
    }
  }
}

#endregion