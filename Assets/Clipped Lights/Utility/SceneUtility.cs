using System.Linq;
using UnityEngine.SceneManagement;

namespace ClippedLights
{
    public class SceneUtility
    {
        public static T[] FindAllComponentsInOpenScenes<T>(bool include_inactive) {
            return Enumerable
                .Range(0, SceneManager.sceneCount)
                .Select(index => SceneManager.GetSceneAt(index))
                .SelectMany(scene => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<T>(include_inactive)))
                .ToArray();
        }
    }
}