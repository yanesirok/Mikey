using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Трансформацию ".pan-canvas" пишет ровно один класс —
    /// MapPanZoomController. Ambient-слой отдаёт ему аддитивное смещение и
    /// множитель зума через SetAmbientOffset, а не пишет канвасу сам: два
    /// писателя одной трансформации дрались бы за неё каждый кадр.
    /// </summary>
    public class MapPanZoomControllerAmbientSourceTests
    {
        private const string ControllerPath = "Assets/UI/Map/MapPanZoomController.cs";
        private const string AmbientPath = "Assets/UI/Map/MapAmbientController.cs";

        [Test]
        public void PanZoomControllerAcceptsAnAmbientOffset()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("public void SetAmbientOffset(", source);
            StringAssert.Contains("public float SecondsSinceLastInput", source);
        }

        [Test]
        public void PanZoomControllerFunnelsEveryWriteThroughOneMethod()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void ApplyCanvasTransform()", source);
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.position ="),
                "Позиция канваса должна писаться ровно в одном месте.");
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.scale ="),
                "Масштаб канваса должен писаться ровно в одном месте.");
        }

        [Test]
        public void AmbientControllerNeverTouchesTheCanvasTransformItself()
        {
            string source = File.ReadAllText(AmbientPath);
            StringAssert.DoesNotContain("_canvas.transform", source);
            StringAssert.Contains("SetAmbientOffset(", source);
        }

        [Test]
        public void ResetTransform_MarksInput_SoTheIdleClockRestartsOnFreshScreenEntry()
        {
            // Находка 1 ревью: _lastInputTime по умолчанию 0 и без сброса на
            // входе на экран отсчитывался бы от запуска приложения, а не от
            // появления карты — простой давно "истёк" бы к первому тику.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void ResetTransform()");
            StringAssert.Contains("MarkInput();", body,
                "ResetTransform must reset the idle clock via MarkInput(), or SecondsSinceLastInput counts from app launch instead of screen entry.");
        }

        [Test]
        public void OnPointerMove_MarksInputUnconditionally_NotOnlyAtTheDragThresholdCrossing()
        {
            // Находка 2 ревью: непрерывный пан длиннее IdleDelaySeconds должен
            // не давать Ken Burns включиться под пальцем — значит MarkInput()
            // обязан стоять ВНЕ (до) ветки "if (!_dragging)", а не только внутри неё.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void OnPointerMove(PointerMoveEvent evt)");

            int dragBranchIndex = body.IndexOf("if (!_dragging)", System.StringComparison.Ordinal);
            Assert.Greater(dragBranchIndex, -1, "Expected the drag-threshold-crossing branch.");

            int markInputIndex = body.IndexOf("MarkInput();", System.StringComparison.Ordinal);
            Assert.Greater(markInputIndex, -1, "Expected a MarkInput() call in OnPointerMove.");
            Assert.Less(markInputIndex, dragBranchIndex,
                "MarkInput() must fire unconditionally before the threshold-crossing branch, on every drag frame — not only once, when the drag threshold is first crossed.");
        }

        [Test]
        public void Update_MarksInputOnPinchContinuation_NotJustAtPinchStart()
        {
            // Находка 2 ревью, вторая половина: пинч длиннее IdleDelaySeconds
            // должен так же не пускать Ken Burns под пальцами — MarkInput()
            // обязан стоять и в ветке продолжения пинча, не только в её старте.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void Update()");

            int continuationStart = body.IndexOf("if (_pinchStartDistance > 0.01f)", System.StringComparison.Ordinal);
            Assert.Greater(continuationStart, -1, "Expected the pinch-continuation branch.");
            int setZoomIndex = body.IndexOf("SetZoom(_pinchStartZoom * ratio);", continuationStart, System.StringComparison.Ordinal);
            Assert.Greater(setZoomIndex, continuationStart, "Expected the pinch-continuation SetZoom call.");

            string continuationBranch = body.Substring(continuationStart, setZoomIndex - continuationStart);
            StringAssert.Contains("MarkInput();", continuationBranch,
                "MarkInput() must be called on every pinch-continuation frame, not only at pinch start — otherwise a pinch longer than IdleDelaySeconds crosses the idle threshold mid-gesture.");
        }

        // ---------- StopInertia: discrete camera placement must kill in-flight coast ----------

        [Test]
        public void StopInertia_ClearsVelocityAndTheInertiaFlag()
        {
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void StopInertia()");
            StringAssert.Contains("_velocityX = 0f;", body);
            StringAssert.Contains("_velocityY = 0f;", body);
            StringAssert.Contains("_inertiaActive = false;", body);
        }

        [Test]
        public void InertiaReset_OnlyLivesInsideStopInertia_NoDuplicatedInlineResets()
        {
            // The velocity/flag reset must route through the shared helper
            // everywhere — a duplicated inline copy at some call site could
            // silently drift out of sync (e.g. gain a field StopInertia()
            // clears but the inline copy forgets).
            string source = File.ReadAllText(ControllerPath);
            Assert.AreEqual(1, CountOccurrences(source, "_velocityX = 0f;"),
                "Expected the velocity reset to live in exactly one place: StopInertia().");
            Assert.AreEqual(1, CountOccurrences(source, "_velocityY = 0f;"),
                "Expected the velocity reset to live in exactly one place: StopInertia().");
            Assert.AreEqual(1, CountOccurrences(source, "_inertiaActive = false;"),
                "Expected the inertia-flag reset to live in exactly one place: StopInertia().");
        }

        [Test]
        public void OnPointerDown_StopsInertia()
        {
            // A fresh touch must kill any in-flight coast immediately, or the
            // still-sliding map would fight the player's new drag.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void OnPointerDown(PointerDownEvent evt)");
            StringAssert.Contains("StopInertia();", body);
        }

        [Test]
        public void OnWheel_StopsInertia()
        {
            // A wheel zoom is real input too — it must break an in-flight
            // coast exactly like a fresh touch does.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void OnWheel(WheelEvent evt)");
            StringAssert.Contains("StopInertia();", body);
        }

        [Test]
        public void Update_PinchStart_StopsInertia_AfterEndDrag()
        {
            // EndDrag(), called when the second finger lands, may itself arm
            // inertia from the drag that preceded it. A pinch start is a
            // fresh gesture, so StopInertia() must run AFTER EndDrag() to
            // override that — otherwise stale, undecayed velocity would
            // survive the pinch and resume panning once it ends.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void Update()");

            int pinchStartIndex = body.IndexOf("_isPinching = true;", System.StringComparison.Ordinal);
            Assert.Greater(pinchStartIndex, -1, "Expected the pinch-start branch in Update().");

            int endDragIndex = body.IndexOf("EndDrag();", pinchStartIndex, System.StringComparison.Ordinal);
            Assert.Greater(endDragIndex, pinchStartIndex, "Expected EndDrag() in the pinch-start branch.");

            int stopInertiaIndex = body.IndexOf("StopInertia();", endDragIndex, System.StringComparison.Ordinal);
            Assert.Greater(stopInertiaIndex, endDragIndex,
                "StopInertia() must run after EndDrag() in the pinch-start branch.");
        }

        [Test]
        public void ResetTransform_StopsInertia()
        {
            // A fresh screen entry snaps the camera discretely — leftover
            // inertia velocity from before the reset must not survive to
            // drive the camera afterward.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void ResetTransform()");
            StringAssert.Contains("StopInertia();", body);
        }

        [Test]
        public void SetViewToSourceFocalPoint_StopsInertia()
        {
            // MapCloudTransitionController plants the destination screen's
            // camera here while fully hidden under cloud cover. A throw made
            // on the source screen just before the transition must not
            // resume once IsTransitioning clears and reveals the new view.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "public void SetViewToSourceFocalPoint(float sourceNormalizedX, float sourceNormalizedY, float zoom)");
            StringAssert.Contains("StopInertia();", body);
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_StopsInertia_BeforeTheAnimationLoop()
        {
            // This coroutine drives the camera itself frame by frame during a
            // Japan<->Okinawa transition — any leftover inertia velocity must
            // be cleared before the loop starts, not fight it mid-flight.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "public IEnumerator AnimateViewToSourceFocalPoint(float targetSourceX, float targetSourceY, float targetZoom, float durationSeconds)");

            int stopIndex = body.IndexOf("StopInertia();", System.StringComparison.Ordinal);
            Assert.Greater(stopIndex, -1, "Expected a StopInertia() call.");

            int loopIndex = body.IndexOf("while (elapsed < durationSeconds)", System.StringComparison.Ordinal);
            Assert.Greater(loopIndex, -1, "Expected the animation loop.");
            Assert.Less(stopIndex, loopIndex, "StopInertia() must run before the animation loop starts driving the camera itself.");
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }
            return count;
        }

        /// <summary>
        /// Извлекает тело метода по его сигнатуре, считая глубину фигурных
        /// скобок — а не наивным поиском первой "}": у Update() и подобных
        /// методов внутри есть вложенные блоки (if/for), и первая же "}"
        /// закрыла бы вложенный блок, а не сам метод.
        /// </summary>
        private static string ExtractMethodBody(string source, string methodSignature)
        {
            int signatureIndex = source.IndexOf(methodSignature, System.StringComparison.Ordinal);
            Assert.Greater(signatureIndex, -1, $"Expected to find '{methodSignature}'.");

            int braceOpen = source.IndexOf('{', signatureIndex);
            int depth = 0;
            int i = braceOpen;
            for (; i < source.Length; i++)
            {
                if (source[i] == '{')
                    depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
            }
            return source.Substring(braceOpen, i - braceOpen + 1);
        }
    }
}
