using System.Collections;
using Mikey.UI.SafeArea;
using Mikey.UI.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Единственный драйвер непрерывного («ambient») движения на обоих экранах
    /// карты: дрейф и параллакс облаков, дыхание бумаги, Ken Burns в простое,
    /// дыхание маркеров.
    ///
    /// <para>
    /// Тикает на 30 Гц, а не по кадру: у ambient-движения периоды в десятки
    /// секунд, разница с 60 Гц невидима, а работы вдвое меньше. По той же
    /// причине на экранах карты поднимается
    /// <see cref="OnDemandRendering.renderFrameInterval"/> — карта статичный
    /// экран, ей незачем полная частота.
    /// </para>
    ///
    /// <para>
    /// Пишет ИСКЛЮЧИТЕЛЬНО transform-свойства и прозрачность. Любая запись
    /// геометрических style-свойств (left/top/width/height) здесь означала бы
    /// полный проход лэйаута каждый тик — см. MapAmbientControllerSourceTests,
    /// который это стережёт.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapAmbientController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const int TickIntervalMs = 33;
        private const int MapRenderFrameInterval = 2;

        private const string JapanScreenId = "map";
        private const string OkinawaScreenId = "mapOkinawa";

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private IMotionSettings _motion;
        private IVisualElementScheduledItem _tick;

        private Coroutine _bindRoutine;
        private bool _bound;
        private bool _onMapScreen;
        private float _elapsedSeconds;

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

            StopTicking();

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            _root = null;
            _motion = null;
            _bound = false;
            _onMapScreen = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapAmbientController] UIDocument root unavailable; ambient not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;
            _motion = GetComponent<IMotionSettings>();

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                OnScreenChanged(_navigator.CurrentScreen);
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            _onMapScreen = screenId == JapanScreenId || screenId == OkinawaScreenId;
            if (_onMapScreen)
                StartTicking();
            else
                StopTicking();
        }

        private void StartTicking()
        {
            if (_root == null || _tick != null)
                return;
            if (_motion != null && _motion.ReducedMotion)
                return;

            _elapsedSeconds = 0f;
            OnDemandRendering.renderFrameInterval = MapRenderFrameInterval;
            _tick = _root.schedule.Execute(Tick).Every(TickIntervalMs);
        }

        private void StopTicking()
        {
            if (_tick != null)
            {
                _tick.Pause();
                _tick = null;
            }
            OnDemandRendering.renderFrameInterval = 1;
        }

        /// <summary>
        /// Один шаг ambient-движения. Пока только копит время — конкретные
        /// каналы (облака, камера, маркеры) добавляются следующими задачами
        /// плана и каждый живёт в своём приватном методе.
        /// </summary>
        private void Tick()
        {
            if (!_bound || !_onMapScreen)
                return;
            if (_motion != null && _motion.ReducedMotion)
            {
                StopTicking();
                return;
            }
            if (MapCloudTransitionController.IsTransitioning)
                return;

            _elapsedSeconds += TickIntervalMs / 1000f;
        }
    }
}
