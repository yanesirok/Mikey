using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.PlayTests
{
    public class SceneLoaderPlayTests
    {
        private const string Fixture = "NavFixtureScene";

        private SceneLoader NewLoader()
        {
            var go = new GameObject("scene-loader-test");
            return go.AddComponent<SceneLoader>();
        }

        [UnityTest]
        public IEnumerator ShowScene_LoadsFixtureAdditively()
        {
            SceneLoader loader = NewLoader();
            loader.ShowScene(Fixture);

            // Wait until the additive load completes.
            float timeout = Time.realtimeSinceStartup + 5f;
            while (!SceneManager.GetSceneByName(Fixture).isLoaded && Time.realtimeSinceStartup < timeout)
                yield return null;

            Assert.IsTrue(SceneManager.GetSceneByName(Fixture).isLoaded, "Fixture scene should be loaded.");
            Assert.AreEqual(Fixture, loader.CurrentHeavyScene);

            Object.Destroy(loader.gameObject);
        }

        [UnityTest]
        public IEnumerator ShowNoScene_UnloadsFixture()
        {
            SceneLoader loader = NewLoader();
            loader.ShowScene(Fixture);
            float t1 = Time.realtimeSinceStartup + 5f;
            while (!SceneManager.GetSceneByName(Fixture).isLoaded && Time.realtimeSinceStartup < t1)
                yield return null;

            loader.ShowNoScene();
            float t2 = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetSceneByName(Fixture).isLoaded && Time.realtimeSinceStartup < t2)
                yield return null;

            Assert.IsFalse(SceneManager.GetSceneByName(Fixture).isLoaded, "Fixture scene should be unloaded.");
            Assert.IsNull(loader.CurrentHeavyScene);

            Object.Destroy(loader.gameObject);
        }
    }
}
