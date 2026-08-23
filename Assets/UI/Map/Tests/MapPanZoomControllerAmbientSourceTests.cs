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

        // ---------- StopRubberBand: same discrete-placement guarantee, for the border rubber band ----------

        [Test]
        public void RubberBandReset_OnlyLivesInsideStopRubberBand_NoDuplicatedInlineResets()
        {
            // Same drift risk as InertiaReset_OnlyLivesInsideStopInertia_...
            // above: a duplicated inline copy of the rubber-band reset could
            // silently fall out of sync with StopRubberBand() (e.g. forget
            // to also clear _rubberBandRoutine).
            string source = File.ReadAllText(ControllerPath);
            Assert.AreEqual(1, CountOccurrences(source, "_rubberBandX = 0f;"),
                "Expected the rubber-band reset to live in exactly one place: StopRubberBand().");
            Assert.AreEqual(1, CountOccurrences(source, "_rubberBandY = 0f;"),
                "Expected the rubber-band reset to live in exactly one place: StopRubberBand().");
        }

        [Test]
        public void RubberBandNeverLeaksIntoTheLogicalPan()
        {
            // The border rubber band must stay purely visual: _panX/_panY
            // may only ever be written by SetPan()'s own ClampPan() calls,
            // never by MapPanZoomMath.RubberBand() — otherwise zoom,
            // inertia and cross-screen transitions would all inherit the
            // "illegal" overscrolled position.
            string source = File.ReadAllText(ControllerPath);

            // The real guarantee: an assignment to _panX/_panY anywhere else
            // in the file — with ANY right-hand side, not just a rubber-band
            // typo — must fail this test. Checking only the ClampPan(...)
            // spelling would pass right through a one-character slip like
            // "_panX = MapPanZoomMath.RubberBand(...)": that substring
            // contains neither "ClampPan(" nor "SetPan()"'s body, so the two
            // narrower checks below wouldn't see it.
            Assert.AreEqual(1, CountOccurrences(source, "_panX = "),
                "Expected _panX to be assigned in exactly one place: SetPan().");
            Assert.AreEqual(1, CountOccurrences(source, "_panY = "),
                "Expected _panY to be assigned in exactly one place: SetPan().");

            Assert.AreEqual(1, CountOccurrences(source, "_panX = MapPanZoomMath.ClampPan("),
                "Expected _panX to be written in exactly one place: SetPan().");
            Assert.AreEqual(1, CountOccurrences(source, "_panY = MapPanZoomMath.ClampPan("),
                "Expected _panY to be written in exactly one place: SetPan().");

            string setPanBody = ExtractMethodBody(source, "private void SetPan(float x, float y)");
            StringAssert.DoesNotContain("RubberBand", setPanBody,
                "SetPan() clamps the logical pan — the rubber band must never mix into that computation.");
        }

        [Test]
        public void SetViewToSourceFocalPoint_MarksInput_SoKenBurnsHonoursTheIdleGraceOnTheNewScreen()
        {
            // A Japan<->Okinawa transition plants the destination camera here,
            // and OnScreenChanged deliberately skips ResetTransform() (the only
            // other MarkInput() on that path) while IsTransitioning. Without a
            // MarkInput() here the idle clock keeps running from the last touch
            // on the SOURCE screen, so the freshly framed camera starts drifting
            // on its first visible frame instead of after the idle grace.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "public void SetViewToSourceFocalPoint(float sourceNormalizedX, float sourceNormalizedY, float zoom)");
            StringAssert.Contains("MarkInput();", body,
                "SetViewToSourceFocalPoint must restart the idle clock, or Ken Burns takes over the transferred view immediately.");
        }

        [Test]
        public void AnimateViewToSourceFocalPoint_MarksInputPerFrame_NotOnlyAtTheStart()
        {
            // This one moves the camera OVER TIME. Marking input once up front
            // would leave only (grace - durationSeconds) of quiet after the
            // camera actually stops, so the mark has to live inside the loop
            // and be repeated on the final settling write.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "public IEnumerator AnimateViewToSourceFocalPoint(float targetSourceX, float targetSourceY, float targetZoom, float durationSeconds)");

            int loopIndex = body.IndexOf("while (elapsed < durationSeconds)", System.StringComparison.Ordinal);
            Assert.Greater(loopIndex, -1, "Expected the animation loop.");
            int yieldIndex = body.IndexOf("yield return null;", loopIndex, System.StringComparison.Ordinal);
            Assert.Greater(yieldIndex, loopIndex, "Expected the loop's frame yield.");

            string loopBody = body.Substring(loopIndex, yieldIndex - loopIndex);
            StringAssert.Contains("MarkInput();", loopBody,
                "MarkInput() must fire on every frame this coroutine drives the camera, not once at the start.");

            string afterLoop = body.Substring(yieldIndex);
            StringAssert.Contains("MarkInput();", afterLoop,
                "The final settling write must mark input too, so the idle grace is measured from where the camera stopped.");
        }

        [Test]
        public void ApplyCanvasTransform_ClampsTheAmbientSum_AndNeverScalesBelowMinZoom()
        {
            // SetPan() clamps the LOGICAL pan against _zoom alone, so at the
            // clamp boundary (which is the resting state on a fresh entry
            // whenever the current chapter sits near a map edge) any ambient
            // offset added afterwards is by definition past the edge. And at
            // _zoom == MinZoom the ambient zoom multiplier bottoms out below 1,
            // shrinking the canvas inside its own viewport — a strip of
            // background on all four sides that no pan clamp can fix.
            string source = File.ReadAllText(ControllerPath);
            string body = ExtractMethodBody(source, "private void ApplyCanvasTransform()");

            StringAssert.Contains("MapPanZoomMath.ClampPan(_panX + _ambientPanX", body,
                "The ambient X offset must be clamped together with the logical pan, not added past the clamp.");
            StringAssert.Contains("MapPanZoomMath.ClampPan(_panY + _ambientPanY", body,
                "The ambient Y offset must be clamped together with the logical pan, not added past the clamp.");
            StringAssert.Contains("MapPanZoomMath.MinZoom", body,
                "The effective scale must never fall below MinZoom, or the ambient zoom dip exposes background on every edge.");

            // The clamp has to be against the EFFECTIVE scale that is actually
            // written to the canvas — clamping against _zoom while scaling by
            // something smaller would leave exactly the gap it was meant to close.
            int scaleAssign = body.IndexOf("float scale =", System.StringComparison.Ordinal);
            int clampIndex = body.IndexOf("MapPanZoomMath.ClampPan(_panX + _ambientPanX", System.StringComparison.Ordinal);
            Assert.Greater(scaleAssign, -1, "Expected the effective scale to be computed here.");
            Assert.Less(scaleAssign, clampIndex, "The effective scale must be computed before it is used to clamp the pan.");
            StringAssert.Contains("scale, viewportWidth", body,
                "The X clamp must use the effective scale, not the bare logical zoom.");
            StringAssert.Contains("scale, viewportHeight", body,
                "The Y clamp must use the effective scale, not the bare logical zoom.");
        }

        [Test]
        public void PanCanvas_IsHintedAsADynamicTransform()
        {
            // The canvas is the one element written every single frame — pan,
            // inertia, rubber band, double tap, opening zoom, paper breath, Ken
            // Burns. Unhinted, each of those writes bakes the transform into
            // vertex data and regenerates the geometry of the whole canvas
            // subtree: art, scrim, cloud layer and every marker button with its
            // label. The design's entire cost argument is conditional on this.
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("_canvas.usageHints = UsageHints.DynamicTransform;", source,
                "The pan canvas must be hinted as a dynamic transform at bind time.");

            string body = ExtractMethodBody(source, "private IEnumerator BindWhenReady()");
            StringAssert.Contains("_canvas.usageHints = UsageHints.DynamicTransform;", body,
                "The hint must be set at bind time, not somewhere that may never run.");
        }

        /// <summary>Shared with the other map source-tests in this assembly — one brace-aware extractor, not a copy per file.</summary>
        internal static int CountOccurrences(string haystack, string needle)
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
        internal static string ExtractMethodBody(string source, string methodSignature)
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
