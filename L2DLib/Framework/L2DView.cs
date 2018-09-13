using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Threading;
using System.ComponentModel;
using System.Runtime.InteropServices;
using L2DLib.Core;
using L2DLib.Interface;

namespace L2DLib.Framework
{
    /// <summary>
    /// Live2D 그래픽 출력과 관련된 기능을 제공합니다.
    /// 본 클래스를 상속하여 추가 기능을 구현할 수 있습니다.
    /// </summary>
    public class L2DView : UserControl, IL2DRenderer
    {
        #region 속성
        /// <summary>
        /// 렌더러가 표시할 모델을 가져오거나 설정합니다.
        /// </summary>
        public L2DModel Model
        {
            get { return _Model; }
            set
            {
                _Model = value;
                render = new L2DRender(value);
            }
        }
        private L2DModel _Model;
        /// <summary>
        /// 렌더링시 투명도를 지원하는지 여부를 나타내는 값을 가져오거나 설정합니다.
        /// </summary>
        public bool AllowTransparency
        {
            get { return _AllowTransparency; }
            set
            {
                _AllowTransparency = value;
                HRESULT.Check(NativeMethods.SetAlpha(value));
            }
        }
        private bool _AllowTransparency = true;

        /// <summary>
        /// 렌더링시 사용할 멀티 샘플링 앤티 앨리어싱 값을 가져오거나 설정합니다.
        /// </summary>
        public uint DesiredSamples
        {
            get { return _DesiredSamples; }
            set
            {
                _DesiredSamples = value;
                HRESULT.Check(NativeMethods.SetNumDesiredSamples(value));
            }
        }
        private uint _DesiredSamples = 4;
        #endregion

        #region 객체
        L2DRender render;
        TimeSpan lastRender;
        DispatcherTimer adapterTimer;
        Image renderHolder = new Image();
        D3DImage renderScene = new D3DImage();
        #endregion

        #region 생성자
        public L2DView()
        {
            if (!DesignerProperties.GetIsInDesignMode(this) && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "L2DNative.dll")))
            {
                Initialized += L2DView_Initialized;
            }
        }

        private void L2DView_Initialized(object sender, EventArgs e)
        {
            renderScene.IsFrontBufferAvailableChanged += RenderScene_OnIsFrontBufferAvailableChanged;
            renderHolder.Source = renderScene;
            Content = renderHolder;

            //HRESULT.Check(NativeMethods.SetAlpha(AllowTransparency));
            //HRESULT.Check(NativeMethods.SetNumDesiredSamples(DesiredSamples));

            Loaded += L2DView_Loaded;
            SizeChanged += L2DView_SizeChanged;

            BeginRenderingScene(false);
        }

        private void L2DView_Loaded(object sender, RoutedEventArgs e)
        {
            HRESULT.Check(
                NativeMethods.SetSize(
                    (uint)renderHolder.ActualWidth,
                    (uint)renderHolder.ActualHeight
                )
            );
        }
        #endregion

        #region 내부 함수
        private bool IsPresented()
        {
            return PresentationSource.FromVisual(this) != null && ActualWidth > 0 && ActualHeight > 0;
        }
        #endregion

        #region 렌더링 이벤트
        private void RenderScene_OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // if the front buffer is available, then WPF has just created a new
            // D3D device, so we need to start rendering our custom scene
            if (renderScene.IsFrontBufferAvailable)
            {
                BeginRenderingScene(true);
            }
            else
            {
                // If the front buffer is no longer available, then WPF has lost its
                // D3D device so there is no reason to waste cycles rendering our
                // custom scene until a new device is created.
                StopRenderingScene();
            }
        }

        private void StopRenderingScene()
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            adapterTimer.Stop();
            
            //while (NativeMethods.D3DTestCooperativeLevel() == (uint)D3DError.DEVICE_LOST)
            //{
            //    Thread.Sleep(5);
            //}
            //if (NativeMethods.D3DTestCooperativeLevel() == (uint)D3DError.DEVICE_NOTRESET)
            //{
            //    NativeMethods.D3DReset();
            //}
            //else
            //{
            //    Console.WriteLine("{0:x8}", NativeMethods.D3DTestCooperativeLevel());
            //}
        }

        /// <summary>
        /// Begin rendering the custom D3D scene into the D3DImage
        /// </summary>
        private void BeginRenderingScene(bool reload = false)
        {
            HRESULT.Check(NativeMethods.SetAlpha(AllowTransparency));
            HRESULT.Check(NativeMethods.SetNumDesiredSamples(DesiredSamples));
            HRESULT.Check(NativeMethods.GetBackBufferNoRef(out var pSurface));
            if (reload)
            {
                Model.Reload();
            }
            // set the back buffer using the new scene pointer
            renderScene.Lock();
            renderScene.SetBackBuffer(D3DResourceType.IDirect3DSurface9, pSurface);
            renderScene.Unlock();

            adapterTimer = new DispatcherTimer();
            adapterTimer.Tick += AdapterTimer_Tick;
            adapterTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            adapterTimer.Start();
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        public virtual void Rendering()
        {

        }

        private void CompositionTarget_Rendering(object sender, EventArgs e)
        {
            RenderingEventArgs args = (RenderingEventArgs)e;

            if (renderScene.IsFrontBufferAvailable && lastRender != args.RenderingTime)
            {
                //IntPtr pSurface = IntPtr.Zero;
                //NativeMethods.GetBackBufferNoRef(out pSurface);
                //if (pSurface != IntPtr.Zero)
                {
                    renderScene.Lock();
                    //renderScene.SetBackBuffer(D3DResourceType.IDirect3DSurface9, pSurface);

                    if (Model != null && Model.IsLoaded)
                    {
                        try
                        {
                            render.BeginRender();
                        }
                        catch (COMException exception)
                        {
                            renderScene.Unlock();
                            //Model.Reload();
                            return;
                        }

                        Model.LoadParam();
                        render.UpdateMotion();
                        render.UpdateEyeBlink();
                        Model.SaveParam();

                        render.UpdateBreath();
                        render.UpdateExpression();

                        Rendering();

                        render.UpdatePhysics();
                        render.UpdatePose();
                        try
                        {
                            render.EndRender();
                        }
                        catch (COMException exception)
                        {
                            //HRESULT.Check(NativeMethods.GetBackBufferNoRef(out var pSurface));
                            //if (pSurface != IntPtr.Zero)
                            //{
                            //    renderScene.SetBackBuffer(D3DResourceType.IDirect3DSurface9, pSurface);
                            //    Model.Reload();
                            //}
                            renderScene.Unlock();
                            //StopRenderingScene();
                            //BeginRenderingScene();
                            return;
                        }
                        //render.EndRender();
                    }

                    renderScene.AddDirtyRect(new Int32Rect(0, 0, renderScene.PixelWidth, renderScene.PixelHeight));
                    renderScene.Unlock();

                    lastRender = args.RenderingTime;
                }
            }
        }

        private void L2DView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //return;
            if (Model != null && Model.IsLoaded && IsPresented())
            {
                HRESULT.Check(
                    NativeMethods.SetSize(
                        (uint)renderHolder.ActualWidth,
                        (uint)renderHolder.ActualHeight
                    )
                );
            }
        }
        #endregion

        #region 어댑터 설정 이벤트
        private void AdapterTimer_Tick(object sender, EventArgs e)
        {
            if (Model != null && Model.IsLoaded && IsPresented())
            {
                NativeStructure.POINT point = new NativeStructure.POINT(renderHolder.PointToScreen(new Point(0, 0)));
                HRESULT.Check(NativeMethods.SetAdapter(point));
            }
        }
        #endregion
    }
}
