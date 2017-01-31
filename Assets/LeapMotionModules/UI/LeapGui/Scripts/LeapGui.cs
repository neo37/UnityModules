﻿using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Leap.Unity.Query;

[ExecuteInEditMode]
public class LeapGui : MonoBehaviour {
  public const string FEATURE_PREFIX = "LEAP_GUI_";
  public const string PROPERTY_PREFIX = "_LeapGui";

  public const string FEATURE_MOVEMENT_TRANSLATION = FEATURE_PREFIX + "MOVEMENT_TRANSLATION";
  public const string FEATURE_MOVEMENT_FULL = FEATURE_PREFIX + "MOVEMENT_FULL";

  public List<LeapGuiFeatureBase> features;

#if UNITY_EDITOR
  public new LeapGuiRenderer renderer;
#else
  public LeapGuiRenderer renderer;
#endif

  [HideInInspector]
  public List<LeapGuiElement> elements;

  [HideInInspector]
  public List<AnchorOfConstantSize> anchors;

  [NonSerialized]
  public List<FeatureSupportInfo> supportInfo;

  private LeapGuiSpace _cachedGuiSpace;
  public LeapGuiSpace space {
    get {
      if (_cachedGuiSpace == null) {
        _cachedGuiSpace = GetComponent<LeapGuiSpace>();
      }
      return _cachedGuiSpace;
    }
  }

  void OnEnable() {
    if (Application.isPlaying) {
      renderer.OnEnableRenderer();
    }
  }

  void OnDisable() {
    if (Application.isPlaying) {
      renderer.OnDisableRenderer();
    }
  }

  void LateUpdate() {
    if (renderer != null) {
      if (Application.isPlaying) {
        renderer.OnUpdateRenderer();
        foreach (var feature in features) {
          feature.isDirty = false;
        }
      } else {
        elements.Clear();
        anchors.Clear();
        rebuildElementList(transform, null);
        rebuildFeatureData();
        rebuildFeatureSupportInfo();

        space.BuildElementData(transform);

        renderer.OnUpdateRendererEditor();
      }
    }
  }

  public LeapGuiRenderer GetRenderer() {
    return renderer;
  }

#if UNITY_EDITOR
  public void SetRenderer(LeapGuiRenderer newRenderer) {
    UnityEditor.Undo.RecordObject(this, "Changed Gui Renderer");

    if (Application.isPlaying) {
      throw new InvalidOperationException("Cannot change renderer at runtime.");
    }

    if (renderer != null) {
      renderer.OnDisableRendererEditor();
      DestroyImmediate(renderer);
      renderer = null;
    }

    renderer = newRenderer;
    renderer.gui = this;

    if (renderer != null) {
      renderer.OnEnableRendererEditor();
    }

    UnityEditor.EditorUtility.SetDirty(this);
  }
#endif

  public bool GetAllFeaturesOfType<T>(List<T> features) where T : LeapGuiFeatureBase {
    this.features.Query().OfType<T>().FillList(features);
    return features.Count != 0;
  }

  public void rebuildElementList(Transform root, AnchorOfConstantSize currAnchor) {
    int count = root.childCount;
    for (int i = 0; i < count; i++) {
      Transform child = root.GetChild(i);

      var childAnchor = currAnchor;

      var anchor = child.GetComponent<AnchorOfConstantSize>();
      if (anchor != null && anchor.enabled) {
        childAnchor = anchor;
        anchors.Add(anchor);
      }

      var element = child.GetComponent<LeapGuiElement>();
      if (element != null && element.enabled) {
        element.anchor = childAnchor;
        element.elementId = elements.Count;
        elements.Add(element);
      }

      rebuildElementList(child, childAnchor);
    }
  }

  private void rebuildFeatureData() {
    foreach (var feature in features) {
      feature.ClearDataObjectReferences();
    }

    for (int i = 0; i < elements.Count; i++) {
      var element = elements[i];

      //First make a map of existing data objects to their correct indexes
      var dataToNewIndex = new Dictionary<LeapGuiElementData, int>();
      foreach (var data in element.data) {
        if (data == null || data.feature == null) {
          continue;
        }

        int index = features.IndexOf(data.feature);
        if (index >= 0) {
          dataToNewIndex[data] = index;
        }
      }

      //Then make sure the data array has enough spaces for all the data objects
      element.data.Fill(features.Count, null);

      //Then re-map the existing data objects to the correct index
      foreach (var pair in dataToNewIndex) {
        element.data[pair.Value] = pair.Key;
      }

      //If data points to a different element, copy it and point it to the correct element
      for (int j = 0; j < element.data.Count; j++) {
        var data = element.data[j];
        if (data != null && data.element != element) {
          data = Instantiate(data);
          data.element = element;
          element.data[j] = data;
        }
      }

      //Then construct new data objects if there is not yet one
      for (int j = 0; j < features.Count; j++) {
        var feature = features[j];

        if (element.data[j] == null) {
          element.data[j] = feature.CreateDataObject(element);
        }

        //Add the correct reference into the feature list
        feature.AddDataObjectReference(element.data[j]);
      }
    }
  }

  private void rebuildFeatureSupportInfo() {
    var typeToFeatures = new Dictionary<Type, List<LeapGuiFeatureBase>>();
    foreach (var feature in features) {
      Type featureType = feature.GetType();
      List<LeapGuiFeatureBase> list;
      if (!typeToFeatures.TryGetValue(featureType, out list)) {
        list = new List<LeapGuiFeatureBase>();
        typeToFeatures[featureType] = list;
      }

      list.Add(feature);
    }


    var featureToInfo = new Dictionary<LeapGuiFeatureBase, FeatureSupportInfo>();

    foreach (var pair in typeToFeatures) {
      var featureType = pair.Key;
      var featureList = pair.Value;
      var infoList = new List<FeatureSupportInfo>().FillEach(featureList.Count, () => FeatureSupportInfo.FullSupport());

      var castList = Activator.CreateInstance(typeof(List<>).MakeGenericType(featureType)) as IList;
      foreach (var feature in featureList) {
        castList.Add(feature);
      }

      try {
        var interfaceType = typeof(ISupportsFeature<>).MakeGenericType(featureType);
        if (!interfaceType.IsAssignableFrom(renderer.GetType())) {
          infoList.FillEach(() => FeatureSupportInfo.Error("This renderer does not support this feature."));
          continue;
        }

        var supportDelegate = interfaceType.GetMethod("GetSupportInfo");

        if (supportDelegate == null) {
          Debug.LogError("Could not find support delegate.");
          continue;
        }

        supportDelegate.Invoke(renderer, new object[] { castList, infoList });
      } finally {
        for (int i = 0; i < featureList.Count; i++) {
          featureToInfo[featureList[i]] = infoList[i];
        }
      }
    }

    supportInfo = new List<FeatureSupportInfo>();
    foreach (var feature in features) {
      supportInfo.Add(featureToInfo[feature]);
    }
  }
}
