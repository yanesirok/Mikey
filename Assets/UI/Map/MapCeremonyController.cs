using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые сцены карты. Отделены от MapAmbientController сознательно:
    /// у них противоположная природа. Ambient крутится вечно и обязан быть
    /// незаметным; церемония играет один раз и обязана быть заметной.
    ///
    /// <para>
    /// <b>Что подключено к настоящим событиям:</b> проявление карты при входе
    /// (ScreenChanged) и клякса на переходе между главами
    /// (MapCloudTransitionController зовёт её в момент подмены экранов).
    /// </para>
    ///
    /// <para>
    /// <b>Что ждёт события:</b> разблокировка главы и штамп «пройдено».
    /// Источника у них сегодня нет — состояние блокировки глав захардкожено в
    /// <see cref="MapMarkerLayout.Chapters"/>, а
    /// <see cref="Mikey.UI.Progression.TutorialProgressState"/> про главы
    /// карты ничего не знает. Обе поставляются готовыми, с публичным входом и
    /// отладочным триггером в инспекторе: подключение к реальному событию —
    /// отдельная задача, когда прогрессия начнёт его отдавать.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapCeremonyController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        private const string JapanScreenId = "map";

        private const string InkWashPlayingClass = "map-inkwash--playing";
        private const string InkWashDissolvingClass = "map-inkwash--dissolving";
        private const string InkWashFastClass = "map-inkwash--fast";
        private const string SealStampedClass = "map-seal--stamped";

        private const float InkWashSeconds = 0.6f;
        private const float TransitionBlotSeconds = 0.3f;
        private const float CloudPartSeconds = 0.7f;
        private const float SealSeconds = 0.3f;
        private const float CloudReturnSeconds = 0.9f;

        /// <summary>
        /// Доля расхождения облаков — восемь процентов от фактической
        /// ширины слоя облаков, не константа в пикселях (см. класс-докстринг
        /// на CloudPartSeconds-петле ниже: планшет и узкий телефон дадут
        /// разный эффект от одной и той же пиксельной константы).
        /// </summary>
        private const float CloudPartFraction = 0.08f;

        /// <summary>
        /// Истина на всё время любой церемонии. Ambient проверяет этот флаг и
        /// уступает — иначе он и церемония писали бы облакам одно и то же
        /// свойство. Тот же приём, что уже применён в
        /// MapCloudTransitionController.IsTransitioning.
        /// </summary>
        public static bool IsPlaying { get; private set; }

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private Coroutine _bindRoutine;
        private Coroutine _ceremonyRoutine;
        private bool _bound;
        private bool _inkWashPlayed;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_ceremonyRoutine != null)
            {
                StopCoroutine(_ceremonyRoutine);
                _ceremonyRoutine = null;
            }
            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            IsPlaying = false;
            _root = null;
            _bound = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapCeremonyController] UIDocument root unavailable; ceremonies not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId != JapanScreenId || _inkWashPlayed)
                return;
            _inkWashPlayed = true;
            PlayMapEntryInkWash();
        }

        /// <summary>Проявление карты чернильным размывом — играет один раз за сессию, при первом входе на мировую карту.</summary>
        public void PlayMapEntryInkWash()
        {
            VisualElement inkWash = _root?.Q<VisualElement>("map-inkwash");
            if (inkWash == null)
                return;

            StartCeremony(PlayInkWashRoutine(inkWash, InkWashSeconds));
        }

        /// <summary>
        /// Короткая чернильная клякса в момент подмены экранов на переходе
        /// Япония-Окинава. Сам переход — честная кинематографичная камера
        /// (см. MapCloudTransitionController), и накрывать её целиком нечем;
        /// клякса лишь маскирует кадр подмены. Живёт 0.3 с, поэтому не
        /// поднимает IsPlaying надолго и ambient не успевает застыть.
        /// </summary>
        public void PlayTransitionBlot()
        {
            VisualElement inkWash = _root?.Q<VisualElement>("map-inkwash");
            if (inkWash == null)
                return;

            StartCeremony(PlayBlotRoutine(inkWash));
        }

        /// <summary>
        /// Сцена разблокировки главы: облака расходятся от её маркера, печать
        /// впечатывается, облака возвращаются. Реального триггера сегодня нет
        /// (см. описание класса) — вызывается вручную и из отладочного пункта
        /// контекстного меню.
        /// </summary>
        public void PlayChapterUnlock(string chapterId)
        {
            if (!MapMarkerLayout.TryGetChapterFocalPoint(chapterId, out float sourceX, out float sourceY))
            {
                Debug.LogWarning($"[MapCeremonyController] Unknown chapter '{chapterId}'; unlock ceremony skipped.", this);
                return;
            }

            StartCeremony(PlayChapterUnlockRoutine(chapterId, sourceX, sourceY));
        }

        /// <summary>Штамп «пройдено» на маркере уровня. Реального триггера сегодня нет — см. описание класса.</summary>
        public void PlayLevelCompleteStamp(int index)
        {
            VisualElement node = _root?.Q<VisualElement>($"level-node-{index}");
            if (node == null)
                return;

            StartCeremony(PlaySealRoutine(node));
        }

        [ContextMenu("Debug: Play chapter unlock (Fukuoka)")]
        public void DebugPlayChapterUnlock() => PlayChapterUnlock(MapMarkerLayout.FukuokaChapterId);

        [ContextMenu("Debug: Play level complete stamp (LVL 1)")]
        public void DebugPlayLevelStamp() => PlayLevelCompleteStamp(1);

        private void StartCeremony(IEnumerator routine)
        {
            if (IsPlaying || !_bound)
                return;
            _ceremonyRoutine = StartCoroutine(RunCeremony(routine));
        }

        private IEnumerator RunCeremony(IEnumerator routine)
        {
            IsPlaying = true;
            yield return routine;
            IsPlaying = false;
            _ceremonyRoutine = null;
        }

        private IEnumerator PlayInkWashRoutine(VisualElement inkWash, float dissolveSeconds)
        {
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
            inkWash.AddToClassList(InkWashPlayingClass);
            yield return null;

            inkWash.AddToClassList(InkWashDissolvingClass);
            yield return new WaitForSecondsRealtime(dissolveSeconds);

            inkWash.RemoveFromClassList(InkWashPlayingClass);
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
        }

        private IEnumerator PlayBlotRoutine(VisualElement inkWash)
        {
            inkWash.AddToClassList(InkWashFastClass);
            yield return PlayInkWashRoutine(inkWash, TransitionBlotSeconds);
            inkWash.RemoveFromClassList(InkWashFastClass);
        }

        private IEnumerator PlayChapterUnlockRoutine(string chapterId, float sourceX, float sourceY)
        {
            VisualElement layer = _root?.Q<VisualElement>("map-cloud-layer");
            if (layer == null)
                yield break;

            // Печать должна встать на узел ИМЕННО той главы, что передал
            // вызывающий, а не на захардкоженную — резолвим имя узла из
            // chapterId, иначе параметр наполовину игнорировался бы.
            VisualElement node = _root.Q<VisualElement>($"chapter-node-{chapterId}");

            // Облака расходятся от точки главы. Пишется translate и
            // прозрачность слоя целиком — раскладка отдельных облаков
            // (MapCloudLayout) при этом не трогается вообще. Доля — от
            // ФАКТИЧЕСКОЙ ширины слоя, а не от придуманной константы: спека
            // задаёт 8% как долю экрана, и планшет с узким телефоном должны
            // разъезжаться на разное число точек при одной и той же доле.
            float layerWidth = layer.resolvedStyle.width;
            float directionX = (sourceX - 0.5f) < 0f ? -1f : 1f;
            float elapsed = 0f;
            while (elapsed < CloudPartSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / CloudPartSeconds);
                layer.style.translate = new Translate(directionX * CloudPartFraction * layerWidth * t, 0f);
                layer.style.opacity = 1f - 0.25f * t;
                yield return null;
            }

            yield return PlaySealRoutine(node);

            elapsed = 0f;
            while (elapsed < CloudReturnSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseInOutCubic(elapsed / CloudReturnSeconds);
                layer.style.translate = new Translate(directionX * CloudPartFraction * layerWidth * (1f - t), 0f);
                layer.style.opacity = 0.75f + 0.25f * t;
                yield return null;
            }

            // Очищаем инлайн вместо записи 0/1: у ".map-cloud-layer" в USS
            // нет своего translate/opacity, так что очистка — и есть покой.
            // Запись буквального нуля перебила бы стиль навсегда и осталась
            // бы верна только пока эти значения не изменятся сами.
            layer.style.translate = StyleKeyword.Null;
            layer.style.opacity = StyleKeyword.Null;
        }

        private IEnumerator PlaySealRoutine(VisualElement anchor)
        {
            if (anchor == null)
                yield break;

            var seal = new VisualElement();
            seal.AddToClassList("map-seal");
            seal.pickingMode = PickingMode.Ignore;
            seal.usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            anchor.Add(seal);

            yield return null;
            seal.AddToClassList(SealStampedClass);
            GetComponent<Mikey.UI.Audio.AudioController>()?.PlaySealStamp();

            yield return new WaitForSecondsRealtime(SealSeconds);
            yield return new WaitForSecondsRealtime(0.6f);

            seal.RemoveFromHierarchy();
        }
    }
}
