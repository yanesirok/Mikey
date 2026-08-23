using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Settings.Tests
{
    /// <summary>
    /// Driven entirely through an in-memory <see cref="FakeMotionSettingsStorage"/> so
    /// no real local storage is touched by this test run — mirrors AudioSettingsStoreTests.
    /// </summary>
    public class MotionSettingsStoreTests
    {
        [Test]
        public void DefaultsToFullMotion()
        {
            var go = new GameObject("motion");
            var store = go.AddComponent<MotionSettingsStore>();
            store.SetStorageForTesting(new FakeMotionSettingsStorage());

            Assert.IsFalse(store.ReducedMotion, "Движение по умолчанию включено полностью.");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PersistsAndRaisesChangedOnlyOnRealChange()
        {
            var storage = new FakeMotionSettingsStorage();
            var go = new GameObject("motion");
            var first = go.AddComponent<MotionSettingsStore>();
            first.SetStorageForTesting(storage);

            int raised = 0;
            first.Changed += () => raised++;

            first.ReducedMotion = true;
            first.ReducedMotion = true;

            Assert.AreEqual(1, raised, "Повторная запись того же значения не должна оповещать.");

            // A fresh store over the SAME storage simulates an app/Editor restart.
            var secondGo = new GameObject("motion2");
            var second = secondGo.AddComponent<MotionSettingsStore>();
            second.SetStorageForTesting(storage);
            Assert.IsTrue(second.ReducedMotion, "Значение должно пережить перезапуск.");

            Object.DestroyImmediate(go);
            Object.DestroyImmediate(secondGo);
        }

        private sealed class FakeMotionSettingsStorage : IMotionSettingsStorage
        {
            private readonly Dictionary<string, bool> _values = new Dictionary<string, bool>();

            public bool TryLoad(string key, out bool value) => _values.TryGetValue(key, out value);

            public void Save(string key, bool value) => _values[key] = value;
        }
    }
}
