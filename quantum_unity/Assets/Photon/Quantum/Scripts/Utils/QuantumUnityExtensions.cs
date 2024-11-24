namespace Quantum {
  using UnityEngine;

  public static class QuantumUnityExtensions {
    
    #region New Find API
    
#if (!UNITY_2020_3_OR_NEWER) || ((UNITY_2021_1_OR_NEWER || UNITY_2021_2_OR_NEWER) && !UNITY_2021_3_OR_NEWER) || (UNITY_2022_1_OR_NEWER && !UNITY_2022_2_OR_NEWER) || UNITY_2020_3_0 || UNITY_2020_3_1 || UNITY_2020_3_2 || UNITY_2020_3_3 || UNITY_2020_3_4 || UNITY_2020_3_5 || UNITY_2020_3_6 || UNITY_2020_3_7 || UNITY_2020_3_8 || UNITY_2020_3_9 || UNITY_2020_3_10 || UNITY_2020_3_11 || UNITY_2020_3_12 || UNITY_2020_3_13 || UNITY_2020_3_14 || UNITY_2020_3_15 || UNITY_2020_3_16 || UNITY_2020_3_17 || UNITY_2020_3_18 || UNITY_2020_3_19 || UNITY_2020_3_20 || UNITY_2020_3_21 || UNITY_2020_3_22 || UNITY_2020_3_23 || UNITY_2020_3_24 || UNITY_2020_3_25 || UNITY_2020_3_26 || UNITY_2020_3_27 || UNITY_2020_3_28 || UNITY_2020_3_29 || UNITY_2020_3_30 || UNITY_2020_3_31 || UNITY_2020_3_32 || UNITY_2020_3_33 || UNITY_2020_3_34 || UNITY_2020_3_35 || UNITY_2020_3_36 || UNITY_2020_3_37 || UNITY_2020_3_38 || UNITY_2020_3_39 || UNITY_2020_3_40 || UNITY_2020_3_41 || UNITY_2020_3_42 || UNITY_2020_3_43 || UNITY_2020_3_44 
    public enum FindObjectsInactive {
      Exclude,
#if UNITY_2020_1_OR_NEWER
      Include,
#endif
    }

    public enum FindObjectsSortMode {
      None,
      InstanceID,
    }

    public static T FindFirstObjectByType<T>() where T : Object {
      return (T)FindFirstObjectByType(typeof(T), FindObjectsInactive.Exclude);
    }

    public static T FindAnyObjectByType<T>() where T : Object {
      return (T)FindAnyObjectByType(typeof(T), FindObjectsInactive.Exclude);
    }

    public static T FindFirstObjectByType<T>(FindObjectsInactive findObjectsInactive) where T : Object {
      return (T)FindFirstObjectByType(typeof(T), findObjectsInactive);
    }

    public static T FindAnyObjectByType<T>(FindObjectsInactive findObjectsInactive) where T : Object {
      return (T)FindAnyObjectByType(typeof(T), findObjectsInactive);
    }

    public static Object FindFirstObjectByType(System.Type type, FindObjectsInactive findObjectsInactive) {
      return Object.FindObjectOfType(type
#if UNITY_2020_1_OR_NEWER
        , findObjectsInactive == FindObjectsInactive.Include
#endif
        );
    }

    public static Object FindAnyObjectByType(System.Type type, FindObjectsInactive findObjectsInactive) {
      return Object.FindObjectOfType(type
#if UNITY_2020_1_OR_NEWER
        , findObjectsInactive == FindObjectsInactive.Include
#endif
        );
    }

    public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object {
      return ConvertObjects<T>(FindObjectsByType(typeof(T), FindObjectsInactive.Exclude, sortMode));
    }

    public static T[] FindObjectsByType<T>(
      FindObjectsInactive findObjectsInactive,
      FindObjectsSortMode sortMode)
      where T : Object {
      return ConvertObjects<T>(FindObjectsByType(typeof(T), findObjectsInactive, sortMode));
    }

    public static Object[] FindObjectsByType(System.Type type, FindObjectsSortMode sortMode) {
      return FindObjectsByType(type, FindObjectsInactive.Exclude, sortMode);
    }

    public static Object[] FindObjectsByType(System.Type type, FindObjectsInactive findObjectsInactive, FindObjectsSortMode sortMode) {
      return Object.FindObjectsOfType(type
#if UNITY_2020_1_OR_NEWER
        , findObjectsInactive == FindObjectsInactive.Include
#endif
        );
    }

    static T[] ConvertObjects<T>(Object[] rawObjects) where T : Object {
      if (rawObjects == null)
        return (T[])null;
      T[] objArray = new T[rawObjects.Length];
      for (int index = 0; index < objArray.Length; ++index)
        objArray[index] = (T)rawObjects[index];
      return objArray;
    }

#endif

    #endregion
  }
}