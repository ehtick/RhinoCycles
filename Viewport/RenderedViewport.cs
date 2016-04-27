/**
Copyright 2014-2015 Robert McNeel and Associates

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
**/

using System;
using System.Runtime.InteropServices;
using System.Threading;
using RhinoCyclesCore;
using Rhino;
using Rhino.PlugIns;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;
using RhinoCyclesCore.Database;
using ssd = System.Diagnostics.Debug;
using System.Drawing;

namespace RhinoCycles
{
	[Guid("69E0C7A5-1C6A-46C8-B98B-8779686CD181")]
	[RealtimeDisplayModeClassInfo("69E0C7A5-1C6A-46C8-B98B-8779686CD181", "Raytraced", "Raytraced")]
	public class RenderedViewport : RealtimeDisplayMode
	{
		private static int g_running_serial;
		private readonly int m_serial;

		private bool m_started;
		private bool m_available;
		private bool m_last_frame_drawn;
		private bool m_frame_available = false;

		private bool m_synchronizing;

		private ViewportRenderEngine m_cycles;
		private ModalRenderEngine m_modal;

		private long m_starttime;
		private int m_samples;
		private int m_maxsamples;
		private string m_status = "";

		public RenderedViewport()
		{
			g_running_serial ++;
			m_serial = g_running_serial;
			ssd.WriteLine($"Initialising a RenderedViewport {m_serial}");
			Plugin.InitialiseCSycles();
			m_available = true;
		}

		public override void CreateWorld(RhinoDoc doc, ViewInfo viewInfo)
		{
			ssd.WriteLine($"CreateWorld {m_serial}");
		}

		private Thread ModalThread;

		public override bool StartRender(uint w, uint h, RhinoDoc doc, ViewInfo rhinoView, ViewportInfo viewportInfo, bool forCapture, RenderWindow renderWindow)
		{
			if(forCapture)
			{
				ModalRenderEngine mre = new ModalRenderEngine(doc, PlugIn.IdFromName("RhinoCycles"), rhinoView, viewportInfo);
				m_cycles = null;
				m_modal = mre;

				mre.Settings = RcCore.It.EngineSettings;
				mre.Settings.UseInteractiveRenderer = false;
				mre.Settings.SetQuality(doc.RenderSettings.AntialiasLevel);

				var rs = new Size((int)w, (int)h);

				mre.RenderWindow = renderWindow;

				mre.RenderWindow.AddWireframeChannel(mre.Doc, mre.ViewportInfo, rs, new Rectangle(0, 0, rs.Width, rs.Height));
				mre.RenderWindow.SetSize(rs);
				mre.RenderDimension = rs;
				mre.Database.RenderDimension = rs;

				mre.Settings.Verbose = true;

				mre.StatusTextUpdated += Mre_StatusTextUpdated;

				mre.Database.LinearWorkflowChanged += DatabaseLinearWorkflowChanged;

				mre.SetFloatTextureAsByteTexture(mre.Settings.RenderDeviceIsOpenCl);

				mre.CreateWorld(); // has to be done on main thread, so lets do this just before starting render session

				ModalThread = new Thread(this.RenderOffscreen)
				{
					Name = "Cycles offscreen viewport rendering with ModalRenderEngine"
				};
				ModalThread.Start(mre);

				return true;
			}

			ssd.WriteLine($"StartRender {m_serial}");
			m_started = true;
			m_available = false; // the renderer hasn't started yet. It'll tell us when it has.
			m_last_frame_drawn = false;

			AsyncRenderContext a_rc = new ViewportRenderEngine(doc.RuntimeSerialNumber, PlugIn.IdFromName("RhinoCycles"), rhinoView);
			m_cycles = (ViewportRenderEngine)a_rc;

			m_cycles.StatusTextUpdated += CyclesStatusTextUpdated; // render engine tells us status texts for the hud
			m_cycles.RenderStarted += m_cycles_RenderStarted; // render engine tells us when it actually is rendering
			m_cycles.StartSynchronizing += m_cycles_StartSynchronizing;
			m_cycles.Synchronized += m_cycles_Synchronized;
			m_cycles.PassRendered += m_cycles_PassRendered;
			m_cycles.Database.LinearWorkflowChanged += DatabaseLinearWorkflowChanged;

			m_cycles.Settings = RcCore.It.EngineSettings;
			m_cycles.Settings.SetQuality(doc.RenderSettings.AntialiasLevel);

			var renderSize = Rhino.Render.RenderPipeline.RenderSize(doc);

			m_cycles.RenderWindow = renderWindow;
			m_cycles.RenderDimension = renderSize;

			m_cycles.Settings.Verbose = true;

			m_maxsamples = m_cycles.Settings.Samples;

			m_cycles.SetFloatTextureAsByteTexture(m_cycles.Settings.RenderDeviceIsOpenCl);

			m_cycles.CreateWorld(); // has to be done on main thread, so lets do this just before starting render session

			m_starttime = GetCurrentTimeStamp();

			m_cycles.RenderThread = new Thread(ViewportRenderEngine.Renderer)
			{
				Name = "A cool Cycles viewport rendering thread"
			};
			m_cycles.RenderThread.Start(m_cycles);

			return true;
		}

		private double m_progress = 0.0;
		private void Mre_StatusTextUpdated(object sender, RenderEngine.StatusTextEventArgs e)
		{
			m_progress = e.Progress;
		}

		public void RenderOffscreen(object o)
		{
			var mre = o as ModalRenderEngine;
			if (mre != null)
			{
				ModalRenderEngine.Renderer(mre);
				//SetCRC(mre.ViewCrc);
				mre.SaveRenderedBuffer(0);
				m_started = true; // we started (and are also ready, though)
				m_available = true;
				m_frame_available = true;
			}
		}

		public override bool IsFrameBufferAvailable(ViewInfo view)
		{
			if (m_cycles != null)
			{
				return m_cycles.Database.AreViewsEqual(GetView(), view);
			}
			else if (m_modal != null)
			{
				return m_frame_available;
			}

			return false;
		}

		void m_cycles_PassRendered(object sender, ViewportRenderEngine.PassRenderedEventArgs e)
		{
			m_frame_available = true;
			if(e.Sample <=1) SetView(e.View);
			SignalRedraw();
		}

		void m_cycles_StartSynchronizing(object sender, EventArgs e)
		{
			m_synchronizing = true;
		}

		void m_cycles_Synchronized(object sender, EventArgs e)
		{
			m_starttime = GetCurrentTimeStamp();
			m_samples = 0;
			m_last_frame_drawn = false;
			m_synchronizing = false;
		}

		void DatabaseLinearWorkflowChanged(object sender, LinearWorkflowChangedEventArgs e)
		{
			ssd.WriteLine($"Setting Gamma {e.Gamma} and ApplyGammaCorrection {e.Lwf.Active}");
			SetUseLinearWorkflowGamma(e.Lwf.Active);
			SetGamma(e.Gamma);
			if (m_cycles != null)
			{
				var imageadjust = m_cycles.RenderWindow.GetAdjust();
				imageadjust.Gamma = e.Gamma;
				m_cycles.RenderWindow.SetAdjust(imageadjust);
			} else if(m_modal!= null)
			{
				var imageadjust = m_modal.RenderWindow.GetAdjust();
				imageadjust.Gamma = e.Gamma;
				m_modal.RenderWindow.SetAdjust(imageadjust);
			}
		}

		void m_cycles_RenderStarted(object sender, ViewportRenderEngine.RenderStartedEventArgs e)
		{
			m_available = true;
		}

		public void ChangeSamples(int samples)
		{
			if (m_maxsamples < samples)
			{
				m_last_frame_drawn = false;
			}
			m_maxsamples = samples;
			m_cycles.ChangeSamples(samples);
		}

		static private long GetCurrentTimeStamp()
		{
			TimeSpan span = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0,DateTimeKind.Utc));
			return (long) span.TotalSeconds;
		}

		void CyclesStatusTextUpdated(object sender, RenderEngine.StatusTextEventArgs e)
		{
			m_samples = e.Samples;

			if (m_samples < 0)
			{
				m_status = "Updating Engine";
			}
			else
			{
				m_status = e.Samples.ToString();
			}
		}

		public override bool ShowCaptureProgress()
		{
			return true;
		}

		public override double CaptureProgress()
		{
			return m_progress;
		}

		public override void GetRenderSize(out int width, out int height)
		{
			if (m_cycles != null)
			{
				width = m_cycles.RenderDimension.Width;
				height = m_cycles.RenderDimension.Height;
			} else
			{
				width = m_modal.RenderDimension.Width;
				height = m_modal.RenderDimension.Height;
			}
		}

		public override bool RestartRender(int width, int height)
		{
			ssd.WriteLine($"RestartRender {m_serial}");
			SetGamma(m_cycles.Database.Gamma);
			m_starttime = GetCurrentTimeStamp();

			return true;
		}

		public override void ShutdownRender()
		{
			m_available = false;
			m_started = false;
			ssd.WriteLine($"!!! === ShutdownRender {m_serial} === !!!");
			m_cycles?.StopRendering();
		}

		public override bool IsRendererStarted()
		{
			//ssd.WriteLine("IsRendererStarted {0}: {1}", m_serial, m_started);
			return m_started;
		}

		public override bool IsRenderframeAvailable()
		{
			//ssd.WriteLine("IsRenderframeAvailable {0}: {1}", m_serial, m_available);
			SetGamma(m_cycles.Database.Gamma);
			bool fa = false;
			if (m_frame_available)
			{
				fa = true;
				m_frame_available = false;
			}
			return m_available && m_cycles.State==State.Rendering && fa;
		}

		public override string HudProductName()
		{
			return "Raytraced";
		}

		public override string HudStatusText()
		{
			return m_status;
		}

		public override int HudMaximumPasses()
		{
			return m_maxsamples;
		}

		public override int LastRenderedPass()
		{
			return m_samples;
		}
		public override int HudLastRenderedPass()
		{
			return m_samples;
		}

		public override bool HudRendererPaused()
		{
			return m_status.Equals("Idle");
		}

		public override bool HudRendererLocked()
		{
			return false;
		}

		public override bool HudShowMaxPasses()
		{
			return false;
		}

		public override bool HudShowPasses()
		{
			return false;
		}

		public override bool HudShowStatusText()
		{
			return true;
		}

		public override long HudStartTime()
		{
			return m_starttime;
		}
	}
}