﻿/**
Copyright 2014-2017 Robert McNeel and Associates

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
using System.Drawing;
using System.Threading;
using ccl;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using RhinoCyclesCore.Core;
using sdd = System.Diagnostics.Debug;

namespace RhinoCyclesCore.RenderEngines
{
	public class ModalRenderEngine : RenderEngine
	{
		public ModalRenderEngine(RhinoDoc doc, Guid pluginId, ViewInfo view, ViewportInfo viewport, Rhino.Display.DisplayPipelineAttributes attributes)
			: base(pluginId, doc.RuntimeSerialNumber, view, viewport, attributes, false)
		{
			ModalRenderEngineCommonConstruct();
		}

		private void ModalRenderEngineCommonConstruct()
		{
			Client = new Client();
			State = State.Rendering;

			Database.ViewChanged += MRE_Database_ViewChanged;

			#region create callbacks for Cycles

			m_update_callback = UpdateCallback;
			m_update_render_tile_callback = null; // UpdateRenderTileCallback;
			m_write_render_tile_callback = WriteRenderTileCallback;
			m_test_cancel_callback = null;
			m_display_update_callback = null; // DisplayUpdateHandler;

			CSycles.log_to_stdout(false);

			#endregion
		}
		private void MRE_Database_ViewChanged(object sender, Database.ChangeDatabase.ViewChangedEventArgs e)
		{
			//ViewCrc = e.Crc;
		}

		private int maxSamples;
		public int requestedSamples { get; set; }

		/// <summary>
		/// Entry point for a new render process. This is to be done in a separate thread.
		/// </summary>
		public void Renderer()
		{
			var cyclesEngine = this;

			var rw = cyclesEngine.RenderWindow;

			if (rw == null) return; // we don't have a window to write to...

			var client = cyclesEngine.Client;
			var size = cyclesEngine.RenderDimension;
			requestedSamples = Attributes?.RealtimeRenderPasses ?? RcCore.It.EngineSettings.Samples;
			maxSamples = requestedSamples;
			cyclesEngine.TriggerCurrentViewportSettingsRequested();

			#region pick a render device
			var renderDevice = RcCore.It.EngineSettings.RenderDevice;

			if (RcCore.It.EngineSettings.Verbose) sdd.WriteLine(
				$"Using device {renderDevice.Name + " " + renderDevice.Description}");
			#endregion
			var scene = CreateScene(client, renderDevice, cyclesEngine);

			cyclesEngine.TriggerCurrentViewportSettingsRequested();
			maxSamples = requestedSamples;

			#region set up session parameters
			var sessionParams = new SessionParameters(client, renderDevice)
			{
				Experimental = false,
				Samples = requestedSamples,
				TileSize = renderDevice.IsCpu ? new Size(32, 32) : new Size(RcCore.It.EngineSettings.TileX, RcCore.It.EngineSettings.TileY),
				TileOrder = TileOrder.Center,
				Threads = (uint)(renderDevice.IsGpu ? 0 : RcCore.It.EngineSettings.Threads),
				ShadingSystem = ShadingSystem.SVM,
				Background = true,
				DisplayBufferLinear = true,
				ProgressiveRefine = true,
				Progressive = true,
			};
			#endregion

			if (cyclesEngine.CancelRender) return;

			#region create session for scene
			cyclesEngine.Session = new Session(client, sessionParams, scene);
			#endregion

			// register callbacks before starting any rendering
			cyclesEngine.SetCallbacks();

			// main render loop, including restarts
			#region start the rendering loop, wait for it to complete, we're rendering now!

			if (cyclesEngine.CancelRender) return;

			cyclesEngine.Database?.Flush();
			var rc = cyclesEngine.UploadData();
			cyclesEngine.Database?.Dispose();
			cyclesEngine.Database = null;

			if (rc)
			{
				cyclesEngine.Session.PrepareRun();

				maxSamples = requestedSamples;

				// lets first reset session
				cyclesEngine.Session.Reset(size.Width, size.Height, requestedSamples);
				// then reset scene
				cyclesEngine.Session.Scene.Reset();
				// and actually start
				bool stillrendering = true;
				var throttle = Math.Max(0, RcCore.It.EngineSettings.ThrottleMs);
				while (stillrendering)
				{
					if (cyclesEngine.IsRendering)
					{
						stillrendering = cyclesEngine.Session.Sample();
						Thread.Sleep(throttle);
					}
					else
					{
						Thread.Sleep(100);
					}
					if (cyclesEngine.IsStopped) break;
				}

				cyclesEngine.Session.EndRun();
			}

			cyclesEngine.CancelRender = true;
			#endregion

			if (RcCore.It.EngineSettings.SaveDebugImages)
			{
				var tmpf = $"{Environment.GetEnvironmentVariable("TEMP")}\\RC_modal_renderer.png";
				cyclesEngine.RenderWindow.SaveRenderImageAs(tmpf, true);
			}

			// we're done now, so lets clean up our session.
			cyclesEngine.Session.Destroy();

			// set final status string and progress to 1.0f to signal completed render
			cyclesEngine.SetProgress(rw,
				$"Render ready {cyclesEngine.RenderedSamples + 1} samples, duration {cyclesEngine.TimeString}", 1.0f);
			cyclesEngine.CancelRender = true;

			// signal the render window we're done.
			rw.EndAsyncRender(RenderWindow.RenderSuccessCode.Completed);
		}

		public bool SupportsPause()
		{
			return true;
		}

		public void ResumeRendering()
		{
			State = State.Rendering;
		}

		public void PauseRendering()
		{
			State = State.Waiting;
		}
	}

}
