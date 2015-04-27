﻿// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Xna.Framework.Input.Touch;

namespace Microsoft.Phone.Controls
{
    /// <summary>
    /// The GestureListener class raises events similar to those provided by the XNA TouchPanel, but it is designed for
    /// Silverlight's event-driven model, rather than XNA's loop/polling model, and it also takes care of the hit testing
    /// and event routing.
    /// </summary>
    public partial class GestureListener
    {
        private static DispatcherTimer _timer;

        private static bool _isInTouch; 
        
        private static List<UIElement> _elements;

        private static Point _gestureOrigin;
        private static bool _useGestureOrigin;
        private static Nullable<Orientation> _gestureOrientation;

        private static Point _cumulativeDelta;
        private static Point _cumulativeDelta2;

        private static Point _finalVelocity;

        private static Point _pinch0;
        private static Point _pinch1;

        private static bool _isPinching;
        private static bool _flicked;
        private static bool _isDragging;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline", Justification="Need static ctor for more than instantiation")]
        static GestureListener()
        {
            Touch.FrameReported += new TouchFrameEventHandler(Touch_FrameReported); 
            
            TouchPanel.EnabledGestures =
                GestureType.Tap |
                GestureType.DoubleTap |
                GestureType.Hold |
                GestureType.FreeDrag |
                GestureType.DragComplete |
                GestureType.Flick |
                GestureType.Pinch |
                GestureType.PinchComplete;

            _timer = new DispatcherTimer() { Interval = TimeSpan.FromMilliseconds(100) };
            _timer.Tick += new EventHandler(OnTimerTick);
        }

        /// <summary>
        /// Handle touch events.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Touch_FrameReported(object sender, TouchFrameEventArgs e)
        {
            bool newIsInTouch = false;
            Point gestureOrigin = new Point(0,0);

            foreach (TouchPoint point in e.GetTouchPoints(null))
            {
                if (point.Action != TouchAction.Up)
                {
                    gestureOrigin = point.Position;
                    newIsInTouch = true;
                    break;
                }
            }

            if (!_isInTouch && newIsInTouch)
            {
                // The user was not in the middle of a gesture, but one has started.
                _gestureOrigin = gestureOrigin;
                _useGestureOrigin = true;
                TouchStart();
            }
            else if (_isInTouch && !newIsInTouch)
            {
                // The user was in the middle of a gesture, but there are no active 
                // touch points anymore.
                TouchComplete();
            }
            else if (_isInTouch)
            {
                // The state has not changed, and the user was in the middle of a gesture.
                TouchDelta();
            }
            else
            {
                // Possible error condition? The user was not in the middle of a 
                // gesture, but a Touch.FrameReported event was received with no
                // active touch points. We should poll the TouchPanel just to be 
                // safe, but do so in such a way that resets the state.
                TouchStart();
            }

            _isInTouch = newIsInTouch;
        }

        /// <summary>
        /// A touch has started.
        /// </summary>
        private static void TouchStart()
        {
            _cumulativeDelta.X = _cumulativeDelta.Y = _cumulativeDelta2.X = _cumulativeDelta2.Y = 0;
            _finalVelocity.X = _finalVelocity.Y = 0;
            _isDragging = _flicked = false;
            _elements = null;
            ProcessTouchPanelEvents();
            _timer.Start();
        }

        /// <summary>
        /// A touch is continuing...
        /// </summary>
        private static void TouchDelta()
        {
            ProcessTouchPanelEvents();
        }

        /// <summary>
        /// A touch has ended.
        /// </summary>
        private static void TouchComplete()
        {
            ProcessTouchPanelEvents();
            _elements = null;
            _gestureOrientation = null;
            _timer.Stop();
        }

        static void OnTimerTick(object sender, EventArgs e)
        {
            ProcessTouchPanelEvents();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode"), System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private static void ProcessTouchPanelEvents()
        {
            Point delta = new Point(0, 0);

            GeneralTransform deltaTransform = null;

            bool clearHitTestElements = false;

            while (TouchPanel.IsGestureAvailable)
            {
                GestureSample sample = TouchPanel.ReadGesture();

                Point samplePosition = sample.Position.ToPoint();
                Point samplePosition2 = sample.Position2.ToPoint();

                Point sampleDelta = sample.Delta.ToPoint();
                GetTranslatedDelta(ref deltaTransform, ref sampleDelta, ref _cumulativeDelta, sample.GestureType != GestureType.Flick);
                Point sampleDelta2 = sample.Delta2.ToPoint();
                GetTranslatedDelta(ref deltaTransform, ref sampleDelta2, ref _cumulativeDelta2, sample.GestureType != GestureType.Flick);

                if (_elements == null)
                {
                    if (!_useGestureOrigin)
                    {
                        _gestureOrigin = samplePosition;
                    }
                    _useGestureOrigin = false;
                    _elements = new List<UIElement>(VisualTreeHelper.FindElementsInHostCoordinates(_gestureOrigin, Application.Current.RootVisual));
                }

                if (!_gestureOrientation.HasValue && (sampleDelta.X != 0 || sampleDelta.Y != 0))
                {
                    _gestureOrientation = Math.Abs(sampleDelta.X) >= Math.Abs(sampleDelta.Y) ? Orientation.Horizontal : Orientation.Vertical;
                }

                switch (sample.GestureType)
                {
                    case GestureType.Tap:
                        RaiseGestureEvent((helper) => helper.Tap, () => new GestureEventArgs(samplePosition), false);
                        break;

                    case GestureType.DoubleTap:
                        RaiseGestureEvent((helper) => helper.DoubleTap, () => new GestureEventArgs(samplePosition), false);
                        break;

                    case GestureType.Hold:
                        RaiseGestureEvent((helper) => helper.Hold, () => new GestureEventArgs(samplePosition), false);
                        break;

                    case GestureType.FreeDrag:
                        if (sampleDelta.X != 0 || sampleDelta.Y != 0)
                        {
                            if (!_isDragging)
                            {
                                RaiseGestureEvent((helper) => helper.DragStarted, () => new DragStartedGestureEventArgs(_gestureOrigin, _gestureOrientation.Value), true);
                                _isDragging = true;
                            }

                            delta.X += sampleDelta.X;
                            delta.Y += sampleDelta.Y;
                        }
                        break;

                    case GestureType.DragComplete:
                        if (!_flicked)
                        {
                            if (delta.X != 0 || delta.Y != 0)
                            {
                                // raise drag
                                RaiseGestureEvent((helper) => helper.DragDelta, () => new DragDeltaGestureEventArgs(_gestureOrigin, delta, _gestureOrientation.Value), false);
                                delta.X = delta.Y = 0;
                            }
                        }

                        if (_isDragging)
                        {
                            RaiseGestureEvent((helper) => helper.DragCompleted, () => new DragCompletedGestureEventArgs(_gestureOrigin, _cumulativeDelta, _gestureOrientation.Value, _finalVelocity), false);
                            clearHitTestElements = true;
                            delta.X = delta.Y = 0;
                        }

                        _cumulativeDelta.X = _cumulativeDelta.Y = 0;
                        _flicked = _isDragging = false;
                        break;

                    case GestureType.Flick:
                        // Do not raise any additional drag events that may be queued.
                        _flicked = true;
                        _finalVelocity = sampleDelta;
                        RaiseGestureEvent((helper) => helper.Flick, () => new FlickGestureEventArgs(_gestureOrigin, sampleDelta), true);
                        break;

                    case GestureType.Pinch:
                        {
                            if (!_isPinching)
                            {
                                _isPinching = true;
                                _pinch0 = samplePosition;
                                _pinch1 = samplePosition2;
                                RaiseGestureEvent((helper) => helper.PinchStarted, () => new PinchStartedGestureEventArgs(_pinch0, _pinch1), true);
                            }

                            RaiseGestureEvent((helper) => helper.PinchDelta, () => new PinchGestureEventArgs(_pinch0, _pinch1, samplePosition, samplePosition2), false);
                        }
                        break;

                    case GestureType.PinchComplete:
                        _isPinching = false;
                        RaiseGestureEvent((helper) => helper.PinchCompleted, () => new PinchGestureEventArgs(_pinch0, _pinch1, samplePosition, samplePosition2), false);
                        _cumulativeDelta.X = _cumulativeDelta.Y = _cumulativeDelta2.X = _cumulativeDelta2.Y = 0;
                        clearHitTestElements = true;                        
                        break;
                }
            }

            if (!_flicked && (delta.X != 0 || delta.Y != 0))
            {
                RaiseGestureEvent((helper) => helper.DragDelta, () => new DragDeltaGestureEventArgs(_gestureOrigin, delta, _gestureOrientation.Value), false);
            }

            if (clearHitTestElements)
            {
                _elements = null;
            }
        }

        private static void GetTranslatedDelta(ref GeneralTransform deltaTransform, ref Point sampleDelta, ref Point cumulativeDelta, bool addToCumulative)
        {
            if (sampleDelta.X != 0 || sampleDelta.Y != 0)
            {
                if (deltaTransform == null)
                {
                    deltaTransform = GetInverseRootTransformNoOffset();
                }

                sampleDelta = deltaTransform.Transform(sampleDelta);

                if (addToCumulative)
                {
                    cumulativeDelta.X += sampleDelta.X;
                    cumulativeDelta.Y += sampleDelta.Y;
                }
            }
        }

        private static GeneralTransform GetInverseRootTransformNoOffset()
        {
            GeneralTransform transform = Application.Current.RootVisual.TransformToVisual(null).Inverse;

            MatrixTransform matrixTransform = transform as MatrixTransform;
            if (matrixTransform != null)
            {
                Matrix matrix = matrixTransform.Matrix; 
                matrix.OffsetX = matrix.OffsetY = 0;
                matrixTransform.Matrix = matrix;
            }

            return transform;
        }

        /// <summary>
        /// This method does all the necessary work to raise a gesture event. It sets the orginal source, does the routing,
        /// handles Handled, and only creates the event args if they are needed.
        /// </summary>
        /// <typeparam name="T">This is the type of event args that will be raised.</typeparam>
        /// <param name="eventGetter">Gets the specific event to raise.</param>
        /// <param name="argsGetter">Lazy creator function for the event args.</param>
        /// <param name="releaseMouseCapture">Indicates whether the mouse capture should be released </param>
        private static void RaiseGestureEvent<T>(Func<GestureListener, EventHandler<T>> eventGetter, Func<T> argsGetter, bool releaseMouseCapture) where T : GestureEventArgs
        {
            T args = null;

            FrameworkElement originalSource = null;
            bool handled = false;

            foreach (FrameworkElement element in _elements)
            {
                if (releaseMouseCapture)
                {
                    element.ReleaseMouseCapture();
                }

                if (!handled)
                {
                    if (originalSource == null)
                    {
                        originalSource = element;
                    }

                    GestureListener helper = GestureService.GetGestureListenerInternal(element, false);
                    if (helper != null)
                    {
                        SafeRaise.Raise(eventGetter(helper), element, () =>
                        {
                            if (args == null)
                            {
                                args = argsGetter();
                                args.OriginalSource = originalSource;
                            }
                            return args;
                        });
                    }

                    if (args != null && args.Handled == true)
                    {
                        handled = true;
                    }
                }
            }
        }
    }
}