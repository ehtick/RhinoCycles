﻿/**
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
using System.Drawing;
using ccl;
using Rhino.Render;
using sdd = System.Diagnostics.Debug;

namespace RhinoCycles
{
	public partial class RenderEngine
	{
		/// <summary>
		/// Entry point for a new render process. This is to be done in a separate thread.
		/// </summary>
		/// <param name="oPipe"></param>
		public static void ModalRenderer(object oPipe)
		{
			var cycles_engine = (RenderEngine)oPipe;

			var client = cycles_engine.Client;
			var rw = cycles_engine.RenderWindow;

			if (rw == null) return; // we don't have a window to write to...

			var size = cycles_engine.RenderDimension;
			var samples = cycles_engine.Settings.Samples;

			cycles_engine.m_measurements.Reset();

			#region pick a render device

			var render_device = cycles_engine.Settings.SelectedDevice == -1
				? Device.FirstCuda
				: Device.GetDevice(cycles_engine.Settings.SelectedDevice);

			if (cycles_engine.Settings.Verbose) sdd.WriteLine(String.Format("Using device {0}", render_device.Name + " " + render_device.Description));
			#endregion

			var scene = CreateScene(client, render_device, cycles_engine);

			#region set up session parameters
			var session_params = new SessionParameters(client, render_device)
			{
				Experimental = false,
				Samples = samples,
				TileSize = render_device.IsCuda ? new Size(256, 256) : new Size(32, 32),
				Threads = (uint)(render_device.IsCuda ? 0 : cycles_engine.Settings.Threads),
				ShadingSystem = ShadingSystem.SVM,
				Background = true,
				ProgressiveRefine = true,
				Progressive = true,
			};
			#endregion

			if (cycles_engine.CancelRender) return;

			#region create session for scene
			cycles_engine.Session = new Session(client, session_params, scene);
			#endregion

			// register callbacks before starting any rendering
			cycles_engine.SetCallbacks();

			// main render loop, including restarts
			#region start the rendering thread, wait for it to complete, we're rendering now!

			cycles_engine.Database.OneShot();
			cycles_engine.m_flush = false;
			cycles_engine.UploadData();

			// lets first reset session
			cycles_engine.Session.Reset((uint)size.Width, (uint)size.Height, (uint)samples);
			// then reset scene
			cycles_engine.Session.Scene.Reset();
			// and actually start
			// we're rendering again
			cycles_engine.Session.Start();
			// ... aaaaand we wait
			cycles_engine.Session.Wait();

			cycles_engine.CancelRender = true;
			#endregion

			// we're done now, so lets clean up our session.
			cycles_engine.Session.Destroy();

			// set final status string and progress to 1.0f to signal completed render
			cycles_engine.SetProgress(rw, String.Format("Render ready {0} samples, duration {1}", cycles_engine.RenderedSamples, cycles_engine.TimeString), 1.0f);

			// signal the render window we're done.
			rw.EndAsyncRender(RenderWindow.RenderSuccessCode.Completed);
		}
	}

}
