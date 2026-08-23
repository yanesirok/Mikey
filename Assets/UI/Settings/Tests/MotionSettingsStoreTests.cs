using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Settings.Tests
{
    public class MotionSettingsStoreTests
    {
        private const string Key = "Mikey.Settings.ReducedMotion";

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(Key);

        [Test]
        public void DefaultsToFullMotion()
        {
            PlayerPrefs.DeleteKey(Key);
            var go = new GameObject("motion");
            var store = go.AddComponent<MotionSettingsStore>();
            Assert.IsFalse(store.ReducedMotion, "Движение по умолчанию включено полностью.");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PersistsAndRaisesChangedOnlyOnRealChange()
        {
            // Свой сброс, а не расчёт на TearDown соседа: тест обязан проходить
            // и когда его гоняют в одиночку по фильтру, первым в свежем процессе.
            PlayerPrefs.DeleteKey(Key);
            var go = new GameObject("motion");
            var store = go.AddComponent<MotionSettingsStore>();

            int raised = 0;
            store.Changed += () => raised++;

            store.ReducedMotion = true;
            store.ReducedMotion = true;

            Assert.AreEqual(1, raised, "Повторная запись того же значения не должна оповещать.");
            Assert.AreEqual(1, PlayerPrefs.GetInt(Key, 0), "Значение должно пережить перезапуск.");
            Object.DestroyImmediate(go);
        }
    }
}
