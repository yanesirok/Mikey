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
        private const string OkinawaScreenId = "mapOkinawa";

        /// <summary>
        /// Ink-wash каждого экрана карты — своя копия под своим именем. Одна
        /// общая копия невозможна: экраны скрываются целиком
        /// (<c>.screen { display: none }</c>), и слой, живущий внутри одного
        /// из них, невидим ровно тогда, когда виден другой.
        /// </summary>
        private const string JapanInkWashName = "map-inkwash";
        private const string OkinawaInkWashName = "okinawa-inkwash";

        private const string InkWashClass = "map-inkwash";

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
        /// Доля расхождения облаков — восемь процентов, а не константа в
        /// пикселях: применяется к фактической ширине слоя облаков (см.
        /// PlayChapterUnlockRoutine), поэтому планшет и узкий телефон
        /// расходятся на разное число точек при одной и той же доле, а не
        /// на одно и то же смещение.
        /// </summary>
        private const float CloudPartFraction = 0.08f;

        /// <summary>
        /// Истина на всё время любой церемонии. Ambient проверяет этот флаг и
        /// уступает — НЕ потому что дерутся за одно и то же свойство одного
        /// элемента (расхождение облаков пишет translate/opacity родителю
        /// map-cloud-layer, а ambient — четырём дочерним облакам;
        /// преобразования складываются, а не перезаписываются), а потому что
        /// ambient не должен продолжать дрейф/дыхание/Ken Burns поверх сцены,
        /// которая обязана читаться как одно поставленное движение. Тот же
        /// приём, что уже применён в MapCloudTransitionController.IsTransitioning.
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

                // Остановленная корутина не доигрывает свой собственный
                // возврат в покой (он сидит в последних строках КАЖДОЙ
                // Play*Routine) -- без этого слой облаков остаётся смещён,
                // ink-wash непрозрачен, а печать висит в иерархии навсегда.
                // Досюда добраться больше некому, поэтому досводим здесь.
                ResetCeremonyVisuals();
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

        /// <summary>
        /// Досводит визуал ЛЮБОЙ прерванной церемонии до состояния покоя --
        /// вызывается только когда её корутину остановили извне (см.
        /// OnDisable), поэтому финальные строки самой Play*Routine не
        /// выполнились. Метёт по всем трём видам возможного мусора сразу,
        /// а не только по тому, что играло: очистка уже-в-покое элемента --
        /// no-op, а знать заранее, какая именно церемония была прервана,
        /// незачем.
        /// </summary>
        private void ResetCeremonyVisuals()
        {
            VisualElement layer = _root?.Q<VisualElement>("map-cloud-layer");
            if (layer != null)
            {
                layer.style.translate = StyleKeyword.Null;
                layer.style.opacity = StyleKeyword.Null;
            }

            // По классу, а не по имени: копий ink-wash столько же, сколько
            // экранов карты, и прерванная церемония могла играть в любой.
            _root?.Query<VisualElement>(className: InkWashClass).ForEach(inkWash =>
            {
                inkWash.RemoveFromClassList(InkWashPlayingClass);
                inkWash.RemoveFromClassList(InkWashDissolvingClass);
                inkWash.RemoveFromClassList(InkWashFastClass);
            });

            // Печать -- динамически созданный VisualElement, который сам
            // убирает себя последней строкой PlaySealRoutine; прерванная
            // корутина туда не доходит, и он остаётся висеть в иерархии
            // узла главы/уровня навсегда.
            _root?.Query<VisualElement>(className: "map-seal").ForEach(seal => seal.RemoveFromHierarchy());
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
            VisualElement inkWash = ResolveInkWash(JapanScreenId);
            if (inkWash == null)
                return;

            StartCeremony(PlayInkWashRoutine(inkWash, InkWashSeconds));
        }

        /// <summary>Ink-wash того экрана карты, о котором идёт речь. Неизвестный экран — null, а не «что-нибудь похожее».</summary>
        private VisualElement ResolveInkWash(string screenId)
        {
            if (screenId == JapanScreenId)
                return _root?.Q<VisualElement>(JapanInkWashName);
            if (screenId == OkinawaScreenId)
                return _root?.Q<VisualElement>(OkinawaInkWashName);
            return null;
        }

        /// <summary>
        /// Короткая чернильная клякса в момент подмены экранов. Зовётся из
        /// MapCloudTransitionController в ОБЕ стороны перехода
        /// (Япония->Окинава и Окинава->Япония), не только в одну. Сам
        /// переход — честная кинематографичная камера (см.
        /// MapCloudTransitionController), и накрывать её целиком нечем;
        /// клякса лишь маскирует кадр подмены. Живёт 0.3 с, поэтому не
        /// поднимает IsPlaying надолго и ambient не успевает застыть.
        ///
        /// <para>
        /// <paramref name="destinationScreenId"/> — экран, который вот-вот
        /// ПОКАЖУТ, тот же самый, что уходит в <c>Show()</c> строкой ниже у
        /// вызывающего. Это и есть суть параметра: экраны скрываются целиком
        /// (<c>.screen { display: none }</c>), и клякса, сыгранная в
        /// исходном экране, растворялась бы в поддереве, которое подмена
        /// прячет тем же кадром — то есть невидимо. Непрозрачное состояние
        /// ставится ещё до <c>Show()</c>, поэтому экран назначения появляется
        /// уже накрытым и только потом проявляется из-под кляксы.
        /// </para>
        /// </summary>
        public void PlayTransitionBlot(string destinationScreenId)
        {
            VisualElement inkWash = ResolveInkWash(destinationScreenId);
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
            // (MapCloudLayout) при этом не трогается вообще (см.
            // CloudPartFraction для того, почему доля берётся от ширины
            // самого слоя).
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
