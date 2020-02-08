using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ClippedLightsEditor {
    public class HandlesScope : IDisposable {
        Color prevColor;
        CompareFunction prevCompareFunction;

        public HandlesScope() {
            prevColor = Handles.color;
            prevCompareFunction = Handles.zTest;
        }

        public void Dispose() {
            Handles.color = prevColor;
            Handles.zTest = prevCompareFunction;
        }
    }
}
