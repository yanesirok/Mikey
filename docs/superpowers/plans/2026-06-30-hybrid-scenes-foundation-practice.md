# Hybrid Scenes — Foundation + Practice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce the hybrid scene architecture by adding a routing layer and an additive scene loader, then prove the pattern end-to-end by moving the Practice screen's heavy content into its own additive scene — without changing behavior for any other screen.

**Architecture:** A pure routing layer (`ScreenRouteTable` + `SceneTransition`) decides, per `screenId`, whether a screen is a lightweight UI **Panel** (toggled in the shared `UIDocument`, as today) or a heavy **Scene** (loaded additively behind its HUD). `ScreenManager` consults the table and drives an `ISceneLoader`. The HUD UXML of heavy screens stays in the shared document, so existing controllers keep binding to the shared root and their tests stay green.

**Tech Stack:** Unity (URP), C#, UI Toolkit (UIElements), Unity Test Framework (NUnit, EditMode + PlayMode), assembly definitions (asmdef).

## Global Constraints

- Render pipeline is **URP** — heavy scenes use URP camera stacking; do not add a second un-stacked `Camera` that fights the base UI camera.
- `IScreenNavigator` (in `Mikey.UI.SafeArea`) is a **stable public contract** — do not change its members; existing controllers depend on it.
- An asmdef assembly **cannot reference `Assembly-CSharp`** (where `ScreenManager` lives). Tests reach `ScreenManager` by reflection: `Type.GetType("ScreenManager, Assembly-CSharp")`. New routing types live in their own asmdef so they are unit-testable.
- `Assembly-CSharp` auto-references asmdefs whose **Auto Referenced** flag is true, so `ScreenManager` can use the new `Mikey.UI.Navigation` types without an explicit reference.
- Navigation convention stays `go-<screenId>`; a navigator must work whether its target is a Panel or a Scene.
- The app must stay runnable and **all tests stay green at every task boundary**.
- Tests run in the Unity Test Runner (Window ▸ General ▸ Test Runner). "Run test" steps are executed there, not from a shell.
- Branch: `frontend/scene-architecture-hybrid`.

---

### Task 1: Baseline — commit the pending ScreenManager startup fix

The working tree already contains a fix that makes `ScreenManager` wire screens from a coroutine once `rootVisualElement` is ready (the original synchronous `OnEnable` could touch a null root and leave every `.screen` hidden). Commit it so the architecture work builds on a clean baseline.

**Files:**
- Modify (already changed in working tree): `Assets/UI/ScreenManager.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ScreenManager` with `private IEnumerator InitializeWhenReady()`, fields `_initialized` / `_initRoutine`, and a `Show(string)` that is safe to call before init.

- [ ] **Step 1: Confirm the working-tree change is the coroutine init**

Run: `git diff --stat` and `git diff Assets/UI/ScreenManager.cs`
Expected: `ScreenManager.cs` shows the `InitializeWhenReady` coroutine, `MaxRootResolveFrames`, `_initialized`, and the `OnDisable` that stops `_initRoutine`.

- [ ] **Step 2: Run the existing navigation tests in Unity Test Runner**

Run (Unity Test Runner ▸ EditMode): `Mikey.UI.SafeArea.Tests` → `ScreenNavigatorTests`, `SceneWiringTests`
Expected: PASS (these drive `Show()` directly / open `SampleScene`, unaffected by the init change).

- [ ] **Step 3: Commit the baseline fix**

```bash
git add Assets/UI/ScreenManager.cs
git commit -m "fix: wire ScreenManager screens from a coroutine once UI root is ready

OnEnable could read UIDocument.rootVisualElement before it existed, leaving
every .screen hidden so no screen ever showed. Defer wiring + initial Show to
a coroutine that waits for the root, matching SafeAreaController/PracticeController.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Pure routing core — `ScreenKind`, `ScreenRoute`, `ScreenRouteTable`, `SceneTransition`

The decision "is this screen a Panel or a Scene, and which scene unloads/loads on a transition" is pure logic with no Unity dependencies. Build and unit-test it first.

**Files:**
- Create: `Assets/UI/Navigation/Mikey.UI.Navigation.asmdef`
- Create: `Assets/UI/Navigation/ScreenKind.cs`
- Create: `Assets/UI/Navigation/ScreenRoute.cs`
- Create: `Assets/UI/Navigation/ScreenRouteTable.cs`
- Create: `Assets/UI/Navigation/SceneTransition.cs`
- Create: `Assets/UI/Navigation/MikeyScreens.cs`
- Create: `Assets/UI/Navigation/Tests/Mikey.UI.Navigation.Tests.asmdef`
- Create: `Assets/UI/Navigation/Tests/ScreenRouteTableTests.cs`
- Create: `Assets/UI/Navigation/Tests/SceneTransitionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `enum Mikey.UI.Navigation.ScreenKind { Panel, Scene }`
  - `sealed class ScreenRoute { string ScreenId; ScreenKind Kind; string SceneName; }`
  - `sealed class ScreenRouteTable` with `void RegisterScene(string screenId, string sceneName)`, `ScreenKind KindOf(string screenId)`, `bool IsScene(string screenId)`, `string SceneNameOf(string screenId)` (returns `null` for panels).
  - `static class SceneTransition` with `static (string unload, string load) Plan(string current, string target)`.
  - `static class MikeyScreens` with `static ScreenRouteTable BuildDefault()` (registers `"practice"` as scene `"Practice"` in Task 6; in this task it registers nothing).

- [ ] **Step 1: Create the runtime asmdef**

Create `Assets/UI/Navigation/Mikey.UI.Navigation.asmdef`:

```json
{
    "name": "Mikey.UI.Navigation",
    "rootNamespace": "Mikey.UI.Navigation",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Write the pure types (no test yet for the trivial enum/data)**

Create `Assets/UI/Navigation/ScreenKind.cs`:

```csharp
namespace Mikey.UI.Navigation
{
    /// <summary>How a screen is realized: a lightweight UI Toolkit panel toggled in the
    /// shared UIDocument, or a heavy additive scene loaded behind its HUD on demand.</summary>
    public enum ScreenKind
    {
        Panel,
        Scene
    }
}
```

Create `Assets/UI/Navigation/ScreenRoute.cs`:

```csharp
namespace Mikey.UI.Navigation
{
    /// <summary>One screen's routing record. <see cref="SceneName"/> is null for panels.</summary>
    public sealed class ScreenRoute
    {
        public string ScreenId { get; }
        public ScreenKind Kind { get; }
        public string SceneName { get; }

        public ScreenRoute(string screenId, ScreenKind kind, string sceneName)
        {
            ScreenId = screenId;
            Kind = kind;
            SceneName = sceneName;
        }
    }
}
```

Create `Assets/UI/Navigation/ScreenRouteTable.cs`:

```csharp
using System.Collections.Generic;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Maps a screenId to how it is realized. Screens default to <see cref="ScreenKind.Panel"/>;
    /// only scene-backed (heavy) screens are registered explicitly, so adding a panel screen needs
    /// no entry here. Pure data — no Unity dependency, fully unit-testable.
    /// </summary>
    public sealed class ScreenRouteTable
    {
        private readonly Dictionary<string, ScreenRoute> _scenes = new Dictionary<string, ScreenRoute>();

        /// <summary>Mark a screen as backed by an additive scene of the given name.</summary>
        public void RegisterScene(string screenId, string sceneName)
        {
            _scenes[screenId] = new ScreenRoute(screenId, ScreenKind.Scene, sceneName);
        }

        public ScreenKind KindOf(string screenId) =>
            _scenes.ContainsKey(screenId) ? ScreenKind.Scene : ScreenKind.Panel;

        public bool IsScene(string screenId) => _scenes.ContainsKey(screenId);

        /// <summary>The additive scene name for a scene screen, or null for a panel screen.</summary>
        public string SceneNameOf(string screenId) =>
            _scenes.TryGetValue(screenId, out ScreenRoute route) ? route.SceneName : null;
    }
}
```

- [ ] **Step 3: Write the failing test for `SceneTransition.Plan`**

Create `Assets/UI/Navigation/Tests/Mikey.UI.Navigation.Tests.asmdef`:

```json
{
    "name": "Mikey.UI.Navigation.Tests",
    "rootNamespace": "Mikey.UI.Navigation.Tests",
    "references": [
        "Mikey.UI.Navigation",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

Create `Assets/UI/Navigation/Tests/SceneTransitionTests.cs`:

```csharp
using NUnit.Framework;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class SceneTransitionTests
    {
        [Test]
        public void SameTarget_PlansNothing()
        {
            var (unload, load) = SceneTransition.Plan("Practice", "Practice");
            Assert.IsNull(unload);
            Assert.IsNull(load);
        }

        [Test]
        public void FromNone_ToScene_LoadsOnly()
        {
            var (unload, load) = SceneTransition.Plan(null, "Practice");
            Assert.IsNull(unload);
            Assert.AreEqual("Practice", load);
        }

        [Test]
        public void FromScene_ToNone_UnloadsOnly()
        {
            var (unload, load) = SceneTransition.Plan("Practice", null);
            Assert.AreEqual("Practice", unload);
            Assert.IsNull(load);
        }

        [Test]
        public void FromScene_ToOtherScene_UnloadsThenLoads()
        {
            var (unload, load) = SceneTransition.Plan("Practice", "CameraTest");
            Assert.AreEqual("Practice", unload);
            Assert.AreEqual("CameraTest", load);
        }
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run (Unity Test Runner ▸ EditMode): `Mikey.UI.Navigation.Tests` → `SceneTransitionTests`
Expected: FAIL / compile error — `SceneTransition` does not exist yet.

- [ ] **Step 5: Implement `SceneTransition`**

Create `Assets/UI/Navigation/SceneTransition.cs`:

```csharp
namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Pure transition planner for the single "active heavy scene" slot. Given the currently
    /// loaded heavy scene (null = none) and the target heavy scene (null = none), returns which
    /// scene to unload and which to load. Null entries mean "do nothing" for that side.
    /// </summary>
    public static class SceneTransition
    {
        public static (string unload, string load) Plan(string current, string target)
        {
            if (current == target)
                return (null, null);
            return (current, target);
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run (Unity Test Runner ▸ EditMode): `SceneTransitionTests`
Expected: PASS (4/4).

- [ ] **Step 7: Write the failing test for `ScreenRouteTable`**

Create `Assets/UI/Navigation/Tests/ScreenRouteTableTests.cs`:

```csharp
using NUnit.Framework;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class ScreenRouteTableTests
    {
        [Test]
        public void UnregisteredScreen_IsPanel()
        {
            var table = new ScreenRouteTable();
            Assert.AreEqual(ScreenKind.Panel, table.KindOf("profile"));
            Assert.IsFalse(table.IsScene("profile"));
            Assert.IsNull(table.SceneNameOf("profile"));
        }

        [Test]
        public void RegisteredScreen_IsScene_WithSceneName()
        {
            var table = new ScreenRouteTable();
            table.RegisterScene("practice", "Practice");
            Assert.AreEqual(ScreenKind.Scene, table.KindOf("practice"));
            Assert.IsTrue(table.IsScene("practice"));
            Assert.AreEqual("Practice", table.SceneNameOf("practice"));
        }

        [Test]
        public void BuildDefault_HasNoSceneScreens_BeforePracticeMigration()
        {
            // Updated in Task 6 once "practice" becomes a scene.
            ScreenRouteTable table = MikeyScreens.BuildDefault();
            Assert.IsFalse(table.IsScene("practice"));
        }
    }
}
```

- [ ] **Step 8: Run the test to verify it fails**

Run (Unity Test Runner ▸ EditMode): `ScreenRouteTableTests`
Expected: FAIL / compile error — `ScreenRouteTable` / `MikeyScreens` not complete.

- [ ] **Step 9: Implement `MikeyScreens.BuildDefault` (empty for now)**

Create `Assets/UI/Navigation/MikeyScreens.cs`:

```csharp
namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Canonical route table for Mikey's screens. Panels need no entry; only heavy
    /// scene-backed screens are registered. Scene screens are added here as they are
    /// migrated out of the shared document into additive scenes.
    /// </summary>
    public static class MikeyScreens
    {
        public static ScreenRouteTable BuildDefault()
        {
            var table = new ScreenRouteTable();
            // Scene-backed screens are registered here during migration (see Task 6).
            return table;
        }
    }
}
```

- [ ] **Step 10: Run the test to verify it passes**

Run (Unity Test Runner ▸ EditMode): `Mikey.UI.Navigation.Tests` (whole suite)
Expected: PASS (all of `ScreenRouteTableTests` + `SceneTransitionTests`).

- [ ] **Step 11: Commit**

```bash
git add Assets/UI/Navigation/Mikey.UI.Navigation.asmdef Assets/UI/Navigation/ScreenKind.cs Assets/UI/Navigation/ScreenRoute.cs Assets/UI/Navigation/ScreenRouteTable.cs Assets/UI/Navigation/SceneTransition.cs Assets/UI/Navigation/MikeyScreens.cs Assets/UI/Navigation/Tests
git commit -m "feat(nav): pure screen-routing core (route table + transition planner)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

> Note: Unity generates `.meta` files for the new folders/files on first import. Include them in the commit (`git add` of the folder picks them up after Unity has imported).

---

### Task 3: Scene loader contract + marker — `ISceneLoader`, `SceneRootMarker`

Define the seam `ScreenManager` will use to drive heavy scenes (so it stays unit-testable with a fake), and a marker component that identifies a loaded heavy scene to tests.

**Files:**
- Create: `Assets/UI/Navigation/ISceneLoader.cs`
- Create: `Assets/UI/Navigation/SceneRootMarker.cs`
- Create: `Assets/UI/Navigation/Tests/SceneRootMarkerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `interface ISceneLoader { string CurrentHeavyScene { get; } void ShowScene(string sceneName); void ShowNoScene(); }`
  - `class SceneRootMarker : MonoBehaviour { string ScreenId { get; } }` (serialized `screenId`).

- [ ] **Step 1: Write `ISceneLoader`**

Create `Assets/UI/Navigation/ISceneLoader.cs`:

```csharp
namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Drives the single "active heavy scene" slot for the router. Implemented by the runtime
    /// <c>SceneLoader</c> MonoBehaviour and by test fakes, so ScreenManager's routing can be
    /// verified without actually loading scenes.
    /// </summary>
    public interface ISceneLoader
    {
        /// <summary>The heavy scene currently loaded, or null if none.</summary>
        string CurrentHeavyScene { get; }

        /// <summary>Ensure exactly this heavy scene is loaded (unloading any other first).</summary>
        void ShowScene(string sceneName);

        /// <summary>Unload any heavy scene so only the persistent App scene remains.</summary>
        void ShowNoScene();
    }
}
```

- [ ] **Step 2: Write the failing test for `SceneRootMarker`**

Create `Assets/UI/Navigation/Tests/SceneRootMarkerTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.Tests
{
    public class SceneRootMarkerTests
    {
        [Test]
        public void Marker_ExposesSerializedScreenId()
        {
            var go = new GameObject("marker-test");
            try
            {
                var marker = go.AddComponent<SceneRootMarker>();
                marker.SetScreenIdForTests("practice");
                Assert.AreEqual("practice", marker.ScreenId);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
```

> The test asmdef from Task 2 is Editor-only and references `UnityEngine.TestRunner`; `GameObject`/`MonoBehaviour` are available. No new reference needed.

- [ ] **Step 3: Run the test to verify it fails**

Run (Unity Test Runner ▸ EditMode): `SceneRootMarkerTests`
Expected: FAIL / compile error — `SceneRootMarker` not defined.

- [ ] **Step 4: Implement `SceneRootMarker`**

Create `Assets/UI/Navigation/SceneRootMarker.cs`:

```csharp
using UnityEngine;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Tags the root of a heavy additive scene with the screenId it backs, so the loader and
    /// tests can recognize a loaded screen scene. One per heavy scene, on a root GameObject.
    /// </summary>
    public sealed class SceneRootMarker : MonoBehaviour
    {
        [SerializeField] private string screenId;

        public string ScreenId => screenId;

        /// <summary>Test-only setter; the field is normally assigned in the Inspector.</summary>
        public void SetScreenIdForTests(string value) => screenId = value;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run (Unity Test Runner ▸ EditMode): `SceneRootMarkerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Assets/UI/Navigation/ISceneLoader.cs Assets/UI/Navigation/SceneRootMarker.cs Assets/UI/Navigation/Tests/SceneRootMarkerTests.cs
git commit -m "feat(nav): scene loader contract (ISceneLoader) and SceneRootMarker

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: `SceneLoader` MonoBehaviour with additive load/unload (PlayMode-tested)

Implement the real loader. The unload/load decision reuses the already-tested `SceneTransition.Plan`; only the async Unity wiring is new, and that is covered by a PlayMode test that loads and unloads a fixture scene.

**Files:**
- Create: `Assets/UI/Navigation/SceneLoader.cs`
- Create: `Assets/UI/Navigation/Tests/PlayMode/Mikey.UI.Navigation.PlayTests.asmdef`
- Create: `Assets/UI/Navigation/Tests/PlayMode/SceneLoaderPlayTests.cs`
- Create (Editor, fixture scene): `Assets/UI/Navigation/Tests/PlayMode/Fixtures/NavFixtureScene.unity`

**Interfaces:**
- Consumes: `SceneTransition.Plan` (Task 2), `ISceneLoader` (Task 3).
- Produces: `sealed class SceneLoader : MonoBehaviour, ISceneLoader` whose `ShowScene`/`ShowNoScene` additively load/unload by scene name and keep `CurrentHeavyScene` accurate.

- [ ] **Step 1: Implement `SceneLoader`**

Create `Assets/UI/Navigation/SceneLoader.cs`:

```csharp
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.UI.Navigation
{
    /// <summary>
    /// Owns the single "active heavy scene" slot. Loads heavy screen scenes additively behind
    /// the persistent App scene's UI and unloads them on exit so their 3D/video/effect memory is
    /// released. The unload/load decision is the pure <see cref="SceneTransition.Plan"/>; this
    /// class only performs the async Unity work and tracks the current scene.
    /// </summary>
    public sealed class SceneLoader : MonoBehaviour, ISceneLoader
    {
        private string _current;        // last scene we asked to be active (null = none)
        private string _pendingTarget;  // most recent requested target, for in-flight coalescing

        public string CurrentHeavyScene => _current;

        public void ShowScene(string sceneName) => Transition(sceneName);

        public void ShowNoScene() => Transition(null);

        private void Transition(string target)
        {
            _pendingTarget = target;
            var (unload, load) = SceneTransition.Plan(_current, target);

            if (unload != null && SceneManager.GetSceneByName(unload).isLoaded)
                SceneManager.UnloadSceneAsync(unload);

            if (load != null)
            {
                if (!SceneManager.GetSceneByName(load).isLoaded)
                {
                    AsyncOperation op = SceneManager.LoadSceneAsync(load, LoadSceneMode.Additive);
                    op.completed += _ => OnLoaded(load);
                }
                else
                {
                    OnLoaded(load);
                }
            }

            _current = target;
        }

        private void OnLoaded(string sceneName)
        {
            // Only adopt as active scene if it is still the intended target (guards rapid nav).
            if (_pendingTarget != sceneName)
                return;
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.isLoaded)
                SceneManager.SetActiveScene(scene);
        }
    }
}
```

- [ ] **Step 2: Create the PlayMode test asmdef**

Create `Assets/UI/Navigation/Tests/PlayMode/Mikey.UI.Navigation.PlayTests.asmdef`:

```json
{
    "name": "Mikey.UI.Navigation.PlayTests",
    "rootNamespace": "Mikey.UI.Navigation.PlayTests",
    "references": [
        "Mikey.UI.Navigation",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

> `includePlatforms` is empty (not Editor-only) so this assembly is available for PlayMode runs.

- [ ] **Step 3: Create the fixture scene (Unity Editor)**

In the Unity Editor:
1. File ▸ New Scene ▸ Empty (or Basic). Remove extra objects so it has just one empty GameObject named `NavFixtureRoot`.
2. Save As → `Assets/UI/Navigation/Tests/PlayMode/Fixtures/NavFixtureScene.unity`.
3. File ▸ Build Settings → Add Open Scenes (so `LoadSceneAsync` by name resolves it). Leave it enabled.

> A fixture scene is required because `SceneManager.LoadSceneAsync(name, Additive)` only resolves scenes listed in Build Settings.

- [ ] **Step 4: Write the failing PlayMode test**

Create `Assets/UI/Navigation/Tests/PlayMode/SceneLoaderPlayTests.cs`:

```csharp
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
```

- [ ] **Step 5: Run to verify it fails, then passes**

Run (Unity Test Runner ▸ **PlayMode**): `SceneLoaderPlayTests`
Expected before Step 1 existed: FAIL. With `SceneLoader` implemented and the fixture in Build Settings: PASS (2/2).
If it fails with "scene could not be loaded", confirm `NavFixtureScene` is in Build Settings (Step 3).

- [ ] **Step 6: Commit**

```bash
git add Assets/UI/Navigation/SceneLoader.cs Assets/UI/Navigation/Tests/PlayMode ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(nav): SceneLoader additive load/unload with PlayMode coverage

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Route `ScreenManager` through the table + loader (behavior unchanged)

Wire `ScreenManager` to consult a `ScreenRouteTable` and drive an `ISceneLoader`. Because `MikeyScreens.BuildDefault()` still registers no scenes, **every screen is a Panel and behavior is identical** — this task is a safe refactor proven by the existing tests plus one new routing test using a fake loader.

**Files:**
- Modify: `Assets/UI/ScreenManager.cs`
- Modify: `Assets/UI/SafeArea/Tests/Mikey.UI.SafeArea.Tests.asmdef`
- Modify: `Assets/UI/SafeArea/Tests/ScreenNavigatorTests.cs`

**Interfaces:**
- Consumes: `ScreenRouteTable`, `MikeyScreens.BuildDefault()`, `ISceneLoader` (Tasks 2–3).
- Produces: `ScreenManager` with `public void ConfigureRouting(ScreenRouteTable routes, ISceneLoader loader)` (test seam) and a `Show(string)` that, on a genuine change, calls `loader.ShowScene(sceneName)` for scene screens and `loader.ShowNoScene()` otherwise. Null routes/loader ⇒ panel-only (unchanged legacy behavior).

- [ ] **Step 1: Add the routing reference to the SafeArea test asmdef**

Modify `Assets/UI/SafeArea/Tests/Mikey.UI.SafeArea.Tests.asmdef` — add `"Mikey.UI.Navigation"` to `references`:

```json
    "references": [
        "Mikey.UI.SafeArea",
        "Mikey.UI.Navigation",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
```

- [ ] **Step 2: Write the failing routing test (with a fake loader)**

Add to `Assets/UI/SafeArea/Tests/ScreenNavigatorTests.cs` — add `using Mikey.UI.Navigation;` at the top, and this nested fake + tests inside the class:

```csharp
        private sealed class FakeSceneLoader : ISceneLoader
        {
            public string CurrentHeavyScene { get; private set; }
            public int ShowSceneCalls;
            public int ShowNoneCalls;
            public string LastSceneName;

            public void ShowScene(string sceneName)
            {
                ShowSceneCalls++;
                LastSceneName = sceneName;
                CurrentHeavyScene = sceneName;
            }

            public void ShowNoScene()
            {
                ShowNoneCalls++;
                CurrentHeavyScene = null;
            }
        }

        private void ConfigureRouting(ScreenRouteTable routes, ISceneLoader loader)
        {
            MethodInfo configure = _screenManagerType.GetMethod("ConfigureRouting");
            Assert.IsNotNull(configure, "ScreenManager must expose ConfigureRouting(ScreenRouteTable, ISceneLoader).");
            configure.Invoke(_screenManager, new object[] { routes, loader });
        }

        [Test]
        public void Show_SceneScreen_DrivesSceneLoader()
        {
            var routes = new ScreenRouteTable();
            routes.RegisterScene("practice", "Practice");
            var loader = new FakeSceneLoader();
            ConfigureRouting(routes, loader);

            Show("practice");

            Assert.AreEqual(1, loader.ShowSceneCalls, "Entering a scene screen must load its scene once.");
            Assert.AreEqual("Practice", loader.LastSceneName);
        }

        [Test]
        public void Show_PanelScreen_AfterScene_UnloadsHeavyScene()
        {
            var routes = new ScreenRouteTable();
            routes.RegisterScene("practice", "Practice");
            var loader = new FakeSceneLoader();
            ConfigureRouting(routes, loader);

            Show("practice");   // scene
            Show("techniques"); // panel

            Assert.AreEqual(1, loader.ShowNoneCalls, "Leaving a scene for a panel must unload the heavy scene.");
            Assert.IsNull(loader.CurrentHeavyScene);
        }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run (Unity Test Runner ▸ EditMode): `ScreenNavigatorTests` → `Show_SceneScreen_DrivesSceneLoader`, `Show_PanelScreen_AfterScene_UnloadsHeavyScene`
Expected: FAIL — `ConfigureRouting` does not exist yet.

- [ ] **Step 4: Implement routing in `ScreenManager`**

In `Assets/UI/ScreenManager.cs`:

(a) Add `using Mikey.UI.Navigation;` beneath the existing usings.

(b) Add fields beside `_initialized`:

```csharp
    private ScreenRouteTable _routes;
    private ISceneLoader _sceneLoader;
```

(c) In `InitializeWhenReady()`, just before `_initialized = true;`, resolve routing defaults if not already configured by a test:

```csharp
        if (_routes == null)
            _routes = MikeyScreens.BuildDefault();
        if (_sceneLoader == null)
            _sceneLoader = GetComponent<SceneLoader>();
```

(d) Add the test/config seam below `OnDisable()`:

```csharp
    /// <summary>
    /// Inject the route table and scene loader. Called automatically at startup with the
    /// canonical table; exposed so tests can supply a custom table and a fake loader.
    /// </summary>
    public void ConfigureRouting(ScreenRouteTable routes, ISceneLoader loader)
    {
        _routes = routes;
        _sceneLoader = loader;
    }
```

(e) Replace the body of `Show(string screenId)` with the panel toggle (unchanged) plus scene driving on genuine change:

```csharp
    public void Show(string screenId)
    {
        foreach (VisualElement screen in _screens)
            screen.style.display = screen.name == screenId ? DisplayStyle.Flex : DisplayStyle.None;

        if (CurrentScreen == screenId)
            return;

        // Drive the heavy-scene slot: load the target's scene, or unload to none for panels.
        // Null routes/loader (e.g. before init, or in pure-panel unit tests) keep legacy behavior.
        if (_sceneLoader != null)
        {
            if (_routes != null && _routes.IsScene(screenId))
                _sceneLoader.ShowScene(_routes.SceneNameOf(screenId));
            else
                _sceneLoader.ShowNoScene();
        }

        CurrentScreen = screenId;
        ScreenChanged?.Invoke(screenId);
    }
```

(f) In `OnDisable()`, after `_initialized = false;`, clear the routing refs so a re-enable re-resolves them:

```csharp
        _routes = null;
        _sceneLoader = null;
```

- [ ] **Step 5: Run the full navigation suite to verify green**

Run (Unity Test Runner ▸ EditMode): `Mikey.UI.SafeArea.Tests` (`ScreenNavigatorTests` + `SceneWiringTests`) and `Mikey.UI.Navigation.Tests`
Expected: PASS. The legacy `ScreenNavigatorTests` still pass because they never call `ConfigureRouting`, so `_sceneLoader` is null and `Show()` keeps its original panel-only behavior.

- [ ] **Step 6: Commit**

```bash
git add Assets/UI/ScreenManager.cs Assets/UI/SafeArea/Tests/ScreenNavigatorTests.cs Assets/UI/SafeArea/Tests/Mikey.UI.SafeArea.Tests.asmdef
git commit -m "feat(nav): route ScreenManager through ScreenRouteTable + ISceneLoader

All screens still resolve to panels (BuildDefault registers no scenes), so
behavior is unchanged; adds a test seam and fake-loader routing coverage.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: Foundation scene `App.unity` + add `SceneLoader` to the UI object

Promote the play scene to the persistent App scene and attach the loader. Still no scene screens registered, so the running app behaves exactly as before — now with the loader present and wired.

**Files:**
- Create (Editor): `Assets/Scenes/App.unity`
- Modify (Editor): `ProjectSettings/EditorBuildSettings.asset` (App at index 0)
- Modify: `Assets/UI/SafeArea/Tests/SceneWiringTests.cs`

**Interfaces:**
- Consumes: `SceneLoader` (Task 4), `ScreenManager` routing (Task 5).
- Produces: `App.unity` containing the `UI` GameObject with `UIDocument` + `ScreenManager` + `SafeAreaController` + `CombineScreenController` + `CameraTestController` + `PracticeController` + **`SceneLoader`**, no missing scripts.

- [ ] **Step 1: Create `App.unity` from the current scene (Unity Editor)**

1. Open `Assets/Scenes/SampleScene.unity`.
2. File ▸ Save As → `Assets/Scenes/App.unity`.
3. Select the `UI` GameObject → Add Component → `Scene Loader` (the `SceneLoader` from `Mikey.UI.Navigation`).
4. Save the scene.

> Keep `SampleScene.unity` for now; the wiring test will switch to `App.unity`. `SampleScene` is removed in a later cleanup plan once nothing references it.

- [ ] **Step 2: Put `App.unity` first in Build Settings (Unity Editor)**

File ▸ Build Settings → ensure `Assets/Scenes/App.unity` is present and dragged to index **0** (it is the bootstrap). Keep `NavFixtureScene` enabled for tests.

- [ ] **Step 3: Update `SceneWiringTests` to target `App.unity` and assert the loader**

In `Assets/UI/SafeArea/Tests/SceneWiringTests.cs`:

(a) Change the scene path constant:

```csharp
        private const string ScenePath = "Assets/Scenes/App.unity";
```

(b) Add a test asserting the loader is wired (place beside the other component tests):

```csharp
        [Test]
        public void UiGameObject_HasSceneLoader()
        {
            GameObject ui = OpenSceneAndFindUi();

            // SceneLoader lives in Mikey.UI.Navigation, which this test asm references
            // (added in Task 5), but look it up by name to match the existing style.
            Assert.IsNotNull(ui.GetComponent("SceneLoader"),
                "UI GameObject must have a SceneLoader (hybrid scene routing).");
        }
```

- [ ] **Step 4: Run the wiring tests**

Run (Unity Test Runner ▸ EditMode): `SceneWiringTests`
Expected: PASS — including `UiGameObject_HasSceneLoader`, `ScreenManager_StartScreen_IsTitle`, and the existing controller checks, now reading `App.unity`.

- [ ] **Step 5: Run the app to confirm the menu still appears**

Open `Assets/Scenes/App.unity`, press Play.
Expected: Title screen shows, navigation works exactly as before (no scene screens yet).

- [ ] **Step 6: Commit**

```bash
git add Assets/Scenes/App.unity Assets/Scenes/App.unity.meta Assets/UI/SafeArea/Tests/SceneWiringTests.cs ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(scenes): add persistent App scene with SceneLoader wired

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Extract Practice's heavy content into the additive `Practice.unity`

The proof of the pattern. Create the Practice additive scene (its 3D/effect content lives here later), register `practice` as a scene screen, and verify entering Practice loads the scene while its HUD keeps working in the shared document.

**Files:**
- Create (Editor): `Assets/Scenes/Screens/Practice.unity`
- Modify: `Assets/UI/Navigation/MikeyScreens.cs`
- Modify: `Assets/UI/Navigation/Tests/ScreenRouteTableTests.cs`
- Create: `Assets/UI/Navigation/Tests/PlayMode/PracticeSceneRoutePlayTests.cs`
- Modify (Editor): `ProjectSettings/EditorBuildSettings.asset` (add `Practice.unity`)

**Interfaces:**
- Consumes: `SceneLoader`, `ScreenManager` routing, `SceneRootMarker` (Tasks 3–6).
- Produces: `Practice.unity` with a root `SceneRootMarker(screenId = "practice")`; `MikeyScreens.BuildDefault()` now registers `practice → "Practice"`.

- [ ] **Step 1: Create `Practice.unity` (Unity Editor)**

1. File ▸ New Scene ▸ Empty.
2. Create an empty GameObject named `PracticeSceneRoot`. Add Component → `Scene Root Marker`; set its **Screen Id** field to `practice`.
3. (Placeholder for now) leave the scene otherwise empty — the 3D character, camera stack, Timeline, video and effects are added in a later content plan. The HUD stays in the shared document.
4. Save As → `Assets/Scenes/Screens/Practice.unity`.
5. File ▸ Build Settings → Add Open Scenes (enable `Practice.unity`).

- [ ] **Step 2: Update the route-table default test (now expects practice as a scene)**

In `Assets/UI/Navigation/Tests/ScreenRouteTableTests.cs`, replace `BuildDefault_HasNoSceneScreens_BeforePracticeMigration`:

```csharp
        [Test]
        public void BuildDefault_RegistersPracticeAsScene()
        {
            ScreenRouteTable table = MikeyScreens.BuildDefault();
            Assert.IsTrue(table.IsScene("practice"));
            Assert.AreEqual("Practice", table.SceneNameOf("practice"));
        }
```

- [ ] **Step 3: Run it to verify it fails**

Run (Unity Test Runner ▸ EditMode): `ScreenRouteTableTests` → `BuildDefault_RegistersPracticeAsScene`
Expected: FAIL — `BuildDefault` registers no scenes yet.

- [ ] **Step 4: Register Practice in `MikeyScreens`**

In `Assets/UI/Navigation/MikeyScreens.cs`, inside `BuildDefault()` before `return table;`:

```csharp
            table.RegisterScene("practice", "Practice");
```

- [ ] **Step 5: Run it to verify it passes**

Run (Unity Test Runner ▸ EditMode): `ScreenRouteTableTests`
Expected: PASS.

- [ ] **Step 6: Write the PlayMode test that entering Practice loads the scene**

Create `Assets/UI/Navigation/Tests/PlayMode/PracticeSceneRoutePlayTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Mikey.UI.Navigation;

namespace Mikey.UI.Navigation.PlayTests
{
    public class PracticeSceneRoutePlayTests
    {
        [UnityTest]
        public IEnumerator ShowScene_Practice_LoadsSceneWithMarker()
        {
            var go = new GameObject("practice-route-test");
            var loader = go.AddComponent<SceneLoader>();

            loader.ShowScene("Practice");

            float timeout = Time.realtimeSinceStartup + 5f;
            while (!SceneManager.GetSceneByName("Practice").isLoaded && Time.realtimeSinceStartup < timeout)
                yield return null;

            Scene practice = SceneManager.GetSceneByName("Practice");
            Assert.IsTrue(practice.isLoaded, "Practice scene should load additively.");

            bool foundMarker = false;
            foreach (GameObject root in practice.GetRootGameObjects())
            {
                var marker = root.GetComponent<SceneRootMarker>();
                if (marker != null && marker.ScreenId == "practice")
                    foundMarker = true;
            }
            Assert.IsTrue(foundMarker, "Practice scene must contain a SceneRootMarker for 'practice'.");

            loader.ShowNoScene();
            float t2 = Time.realtimeSinceStartup + 5f;
            while (SceneManager.GetSceneByName("Practice").isLoaded && Time.realtimeSinceStartup < t2)
                yield return null;

            Object.Destroy(go);
        }
    }
}
```

- [ ] **Step 7: Run the PlayMode test**

Run (Unity Test Runner ▸ PlayMode): `PracticeSceneRoutePlayTests`
Expected: PASS. If "scene could not be loaded", confirm `Practice.unity` is enabled in Build Settings (Step 1.5).

- [ ] **Step 8: Manual verification in the Editor**

1. Open `Assets/Scenes/App.unity`, press Play.
2. Navigate Title → Begin → Enter the Dojo → Map/Techniques → the "1 · Front Stance" lesson (`go-practice`).
3. Confirm: the Practice HUD (score, cue pill, Begin/Complete) appears as before, AND in the Hierarchy a second scene `Practice` is loaded additively (its `PracticeSceneRoot` visible).
4. Navigate back to Lessons (`go-techniques`); confirm the `Practice` scene unloads from the Hierarchy.

Expected: HUD behaves exactly as before; the `Practice` scene loads on entry and unloads on exit.

- [ ] **Step 9: Commit**

```bash
git add Assets/Scenes/Screens/Practice.unity Assets/Scenes/Screens/Practice.unity.meta Assets/UI/Navigation/MikeyScreens.cs Assets/UI/Navigation/Tests/ScreenRouteTableTests.cs Assets/UI/Navigation/Tests/PlayMode/PracticeSceneRoutePlayTests.cs ProjectSettings/EditorBuildSettings.asset
git commit -m "feat(scenes): extract Practice into an additive scene behind its HUD

Entering 'practice' now loads Scenes/Screens/Practice.unity additively and
leaving it unloads it; the Practice HUD stays in the shared UIDocument so
PracticeController is unchanged. Proves the hybrid scene-loading pattern.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## What this slice deliberately defers

- **UXML split** of the 893-line `MikeyApp.uxml` into per-screen files (own follow-up plan; addresses the file-size/merge-conflict motivation, mechanically large, independent of scene routing).
- **ScriptableObject state layer** (`PlayerProfileState`, `ProgressionState`) — not needed until a migrated screen reads/writes cross-scene state; YAGNI for Practice v1.
- **Remaining heavy screens** (`camTest`, `title`, `intro`, `map`) — each repeats Task 7's pattern (create scene + marker, `RegisterScene`, PlayMode test) once their 3D content exists.
- **Real Practice 3D content** (character, camera stack, Timeline, video, effects) — a content plan; this slice only proves load/unload with an empty marked scene.
- **Removing `SampleScene.unity`** — cleanup once nothing references it.

## Self-Review

**Spec coverage:** Hybrid model (persistent App scene + additive heavy scenes) → Tasks 4,6,7. Router with Panel/Scene kinds keeping `IScreenNavigator` stable → Tasks 2,5. HUD-stays-in-shared-document risk mitigation → Tasks 5,7. Build Settings / active-scene / rapid-nav guard → Task 4. Test strategy (pure unit + PlayMode load/unload + extended `SceneWiringTests`) → Tasks 2,4,6,7. Deferred items (UXML split, state layer, other heavy screens) explicitly listed and matched to the spec's "out of scope".

**Placeholder scan:** No "TBD/TODO/handle edge cases" in steps; every code step shows complete code; scene/Editor steps give exact menu paths and field values.

**Type consistency:** `ScreenRouteTable` members (`RegisterScene`, `KindOf`, `IsScene`, `SceneNameOf`) consistent across Tasks 2,5,7. `ISceneLoader` (`CurrentHeavyScene`, `ShowScene`, `ShowNoScene`) consistent across Tasks 3,4,5,7. `SceneTransition.Plan` signature consistent in Tasks 2,4. `ScreenManager.ConfigureRouting(ScreenRouteTable, ISceneLoader)` consistent in Tasks 5 (impl + test). `SceneRootMarker.ScreenId` / `SetScreenIdForTests` consistent in Tasks 3,7. Scene name `"Practice"` ↔ file `Assets/Scenes/Screens/Practice.unity` consistent in Tasks 6,7.
