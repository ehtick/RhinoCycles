﻿/**
Copyright 2014-2016 Robert McNeel and Associates

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
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;
using RhinoCyclesCore.Database;
using sdd = System.Diagnostics.Debug;
using CclLight = ccl.Light;
using CclMesh = ccl.Mesh;
using CclObject = ccl.Object;

namespace RhinoCyclesCore
{

	public enum State
	{
		Waiting,
		Uploading,
		Rendering,
		Stopped
	}

	/// <summary>
	/// The actual render engine, ready for asynchronous work in Rhino.
	/// </summary>
	public partial class RenderEngine : AsyncRenderContext
	{
		private readonly object m_flushlock = new object();

		/// <summary>
		/// Lock object to protect buffer access.
		/// </summary>
		public readonly object display_lock = new object();

		protected CreatePreviewEventArgs m_preview_event_args;

		protected Guid m_plugin_id = Guid.Empty;

		/// <summary>
		/// Reference to the client representation of this render engine instance.
		/// </summary>
		public Client Client { get; set; }

		/// <summary>
		/// True when State.Rendering
		/// </summary>
		public bool IsRendering { get { return State == State.Rendering; } }
		/// <summary>
		/// True when State.Uploading
		/// </summary>
		public bool IsUploading { get { return State == State.Uploading; } }
		/// <summary>
		/// True when State.Waiting
		/// </summary>
		public bool IsWaiting { get { return State == State.Waiting; } }
		/// <summary>
		/// True when State.IsStopped
		/// </summary>
		public bool IsStopped {  get { return State == State.Stopped; } }
		/// <summary>
		/// Current render engine state.
		/// </summary>
		public State State { get; set; }

		/// <summary>
		/// Reference to the session of this render engine instance.
		/// </summary>
		public Session Session = null;

		/// <summary>
		/// Reference to the thread in which this render engine session lives.
		/// </summary>
		public Thread RenderThread { get; set; }

		/// <summary>
		/// Reference to the RenderWindow into which we're rendering.
		/// 
		/// Can be null, for instance in the case of material preview rendering
		/// </summary>
		public RenderWindow RenderWindow { get; set; }

		/// <summary>
		/// Reference to the bitmap we're rendering into.
		/// 
		/// This is used when rendering material previews.
		/// </summary>
		public Bitmap RenderBitmap { get; set; }

		/// <summary>
		/// Set to true when the render session should be cancelled - used for preview job cancellation
		/// </summary>
		public bool CancelRender { get; set; }

		public int RenderedSamples;

		public string TimeString;

		protected CSycles.UpdateCallback m_update_callback;
		protected CSycles.RenderTileCallback m_update_render_tile_callback;
		protected CSycles.RenderTileCallback m_write_render_tile_callback;
		protected CSycles.TestCancelCallback m_test_cancel_callback;
		protected CSycles.DisplayUpdateCallback m_display_update_callback;

		public class SamplesChangedEventArgs : EventArgs
		{
			public int Count { get; private set; }

			public SamplesChangedEventArgs(int count)
			{
				Count = count;
			}
		}

		public event EventHandler<SamplesChangedEventArgs> SamplesChanged;
		public void TriggerSamplesChanged(int samples)
		{
			SamplesChanged?.Invoke(this, new SamplesChangedEventArgs(samples));
		}


		public event EventHandler ChangesReady;
		public void TriggerChangesReady()
		{
			ChangesReady?.Invoke(this, EventArgs.Empty);
		}

		protected bool m_flush;
		/// <summary>
		/// Flag set to true when a flush on the changequeue is needed.
		///
		/// Setting of Flush is protected with a lock. Getting is not.
		/// </summary>
		public bool Flush
		{
			get
			{
				return m_flush;
			}
			set
			{
				lock (m_flushlock)
				{
					m_flush = value;
				}
				TriggerChangesReady();
			}
		}

		/// <summary>
		/// Our instance of the change queue. This is our access point for all
		/// data. The ChangeQueue mechanism will push data to it, record it
		/// with all necessary book keeping to track the data relations between
		/// Rhino and Cycles.
		/// </summary>
		public ChangeDatabase Database { get; set; }

		/// <summary>
		/// OpenCL doesn't properly support HDRi textures in the environment,
		/// so read them as byte textures instead.
		/// </summary>
		public void SetFloatTextureAsByteTexture(bool floatAsByte)
		{
			Database.SetFloatTextureAsByteTexture(floatAsByte);
		}

		/// <summary>
		/// Return true if any change has been received through the changequeue
		/// </summary>
		/// <returns>true if any changes have been received.</returns>
		private bool HasSceneChanges()
		{
			return Database.HasChanges();
		}

		/// <summary>
		/// Check if we should change render engine status. If the changequeue
		/// has notified us of any changes Flush will be true. If we're rendering
		/// then move to State.Halted and cancel our current render progress.
		/// </summary>
		public void CheckFlushQueue()
		{
			// not rendering, nor flush needed, bail
			if (State != State.Rendering || Database == null || !Flush) return;

			// We've been told we need to flush, so cancel current render
			//State = State.Halted;
			// acquire lock while flushing queue and uploading any data
			lock (m_flushlock)
			{
				// flush the queue
				Database.Flush();

				// if we've got actually changes we care about
				// change state to signal need for uploading
				if (HasSceneChanges())
				{
					State = State.Uploading;
					if (!m_interactive && Session != null) Session.Cancel("Scene changes detected.\n");
				}

				// reset flush flag directly, since we already have lock.
				m_flush = false;
			}
		}

		protected readonly uint m_doc_serialnumber;
		protected readonly ViewInfo m_view;
		private readonly bool m_interactive;

		public RhinoDoc Doc
		{
			get { return RhinoDoc.FromRuntimeSerialNumber(m_doc_serialnumber); }
		}

		private readonly ViewportInfo m_vp;
		public ViewportInfo ViewportInfo
		{
			get
			{
				if (m_vp != null) return m_vp;
				return m_view.Viewport;
			}
		}

		/// <summary>
		/// Render engine implementations that need to keep track of views
		/// for instance to signal when a frame is ready for that particular
		/// view.
		/// 
		/// Generally such engines want to register an event handler to
		/// Database.ViewChanged to record the new ViewInfo here.
		/// </summary>
		public ViewInfo View { get; set; }

#region CONSTRUCTORS

		private void RegisterEventHandler()
		{
			Database.MaterialShaderChanged += Database_MaterialShaderChanged;
			Database.LightShaderChanged += Database_LightShaderChanged;
			Database.FilmUpdateTagged += Database_FilmUpdateTagged;
		}
		public RenderEngine(Guid pluginId, uint docRuntimeSerialnumber, ViewInfo view, ViewportInfo vp, bool interactive)
		{
			m_plugin_id = pluginId;
			m_doc_serialnumber = docRuntimeSerialnumber;
			m_view = view;
			m_vp = vp;
			m_interactive = interactive;
			Database = new ChangeDatabase(m_plugin_id, this, m_doc_serialnumber, m_view, !m_interactive);

			RegisterEventHandler();
		}

		public RenderEngine(Guid pluginId, CreatePreviewEventArgs previewEventArgs, bool interactive)
		{
			m_preview_event_args = previewEventArgs;
			Database = new ChangeDatabase(pluginId, this, m_preview_event_args);

			RegisterEventHandler();
		}

#endregion

		/// <summary>
		/// Tell our changequeue instance to initialise world.
		/// </summary>
		public void CreateWorld()
		{
			Database.CreateWorld();
		}

		/// <summary>
		/// True if rendering for preview
		/// </summary>
		/// <returns></returns>
		public bool IsPreview()
		{
			return Database.IsPreview;
		}

		/// <summary>
		/// Flush
		/// </summary>
		public void FlushIt()
		{
			Database.Flush();
		}

		public void TestCancel(uint sid)
		{
			if (State == State.Stopped) return;

			if (m_preview_event_args != null)
			{
				if (m_preview_event_args.Cancel)
				{
					CancelRender = true;
					Session.Cancel("Preview Cancelled");
				}
			}
		}

		public class StatusTextEventArgs
		{
			public StatusTextEventArgs(string s, float progress, int samples)
			{
				StatusText = s;
				Progress = progress;
				Samples = samples;
			}

			public string StatusText { get; private set; }
			public float Progress { get; private set; }
			public int Samples { get; private set; }
		}

		public event EventHandler<StatusTextEventArgs> StatusTextUpdated;

		/// <summary>
		/// Tell engine to fire StatusTextEvent with given arguments
		/// </summary>
		/// <param name="e"></param>
		public void TriggerStatusTextUpdated(StatusTextEventArgs e)
		{
			StatusTextUpdated?.Invoke(this, e);
		}

		/// <summary>
		/// Handle status updates
		/// </summary>
		/// <param name="sid"></param>
		public void UpdateCallback(uint sid)
		{
			if (State == State.Stopped) return;

			var status = CSycles.progress_get_status(Client.Id, sid);
			var substatus = CSycles.progress_get_substatus(Client.Id, sid);
			RenderedSamples = CSycles.progress_get_sample(Client.Id, sid);
			int tile;
			float progress;
			double total_time, render_time, tile_time;
			CSycles.progress_get_tile(Client.Id, sid, out tile, out total_time, out render_time, out tile_time);
			CSycles.progress_get_progress(Client.Id, sid, out progress, out total_time, out render_time, out tile_time);
			int hr = ((int)total_time) / (60 * 60);
			int min = (((int)total_time) / 60) % 60;
			int sec = ((int)total_time) % 60;
			int hun = ((int)(total_time * 100.0)) % 100;

			if (!substatus.Equals(string.Empty)) status = status + ": " + substatus;

			TimeString = String.Format("{0}h {1}m {2}.{3}s", hr, min, sec, hun);

			status = String.Format("{0} {1}", status, TimeString);

			// don't set full 100% progress here yet, because that signals the renderwindow the end of async render
			if (progress >= 0.9999f) progress = 1.0f;
			if (Settings.Samples == ushort.MaxValue) progress = -1.0f;
			if (null != RenderWindow) RenderWindow.SetProgress(status, progress);

			TriggerStatusTextUpdated(new StatusTextEventArgs(status, progress, RenderedSamples>0 ? (RenderedSamples+1) : RenderedSamples));

			if(!m_interactive) CheckFlushQueue();
		}

		/// <summary>
		///  Clamp color so we get valid values for system bitmap
		/// </summary>
		/// <param name="ch"></param>
		/// <returns></returns>
		public static int ColorClamp(int ch)
		{
			if (ch < 0) return 0;
			return ch > 255 ? 255 : ch;
		}

		/// <summary>
		/// Update the RenderWindow or RenderBitmap with the updated tile from
		/// Cycles render progress.
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="tx"></param>
		/// <param name="ty"></param>
		/// <param name="tw"></param>
		/// <param name="th"></param>
		public void DisplayBuffer(uint sessionId, uint tx, uint ty, uint tw, uint th)
		{
			if (State == State.Stopped) return;
			var start = DateTime.Now;
			var rg = RenderBitmap;
			if (RenderWindow != null)
			{
				using (var channel = RenderWindow.OpenChannel(RenderWindow.StandardChannels.RGBA))
				{
					if (channel != null)
					{
						var pixelbuffer = new PixelBuffer(CSycles.session_get_buffer(Client.Id, sessionId));
						var size = Client.Scene.Camera.Size;
						var rect = new Rectangle((int) tx, (int) ty, (int) tw, (int) th);
						channel.SetValues(rect, size, pixelbuffer);
						RenderWindow.InvalidateArea(rect);
					}
				}
			}
			else if (rg != null)
			{
				uint buffer_size;
				uint buffer_stride;
				var width = RenderDimension.Width;
				CSycles.session_get_buffer_info(Client.Id, sessionId, out buffer_size, out buffer_stride);
				var pixels = CSycles.session_copy_buffer(Client.Id, sessionId, buffer_size);
				for (var x = (int)tx; x < (int)(tx + tw); x++)
				{
					for (var y = (int)ty; y < (int)(ty + th); y++)
					{
						var i = y * width * 4 + x * 4;
						var r = pixels[i];
						var g = pixels[i + 1];
						var b = pixels[i + 2];
						var a = pixels[i + 3];

						r = Math.Min(Math.Abs(r), 1.0f);
						g = Math.Min(Math.Abs(g), 1.0f);
						b = Math.Min(Math.Abs(b), 1.0f);
						a = Math.Min(Math.Abs(a), 1.0f);

						var c4_f = new Color4f(r, g, b, a);
						rg.SetPixel(x, y, c4_f.AsSystemColor());
					}
				}
			}
			var diff = (DateTime.Now - start).TotalMilliseconds;
		}

		/// <summary>
		/// Callback for debug logging facility. Will be called only for Debug builds of ccycles.dll
		/// </summary>
		/// <param name="msg"></param>
		public static void LoggerCallback(string msg)
		{
#if DEBUG
			sdd.WriteLine(String.Format("DBG: {0}", msg));
#endif
		}

		/// <summary>
		/// Handle write render tile callback
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="w"></param>
		/// <param name="h"></param>
		/// <param name="depth"></param>
		public void WriteRenderTileCallback(uint sessionId, uint x, uint y, uint w, uint h, uint depth, int startSample, int numSamples, int sample, int resolution)
		{
			if (State == State.Stopped) return;
			DisplayBuffer(sessionId, x, y, w, h);
		}

		/// <summary>
		/// Handle update render tile callback
		/// </summary>
		/// <param name="sessionId"></param>
		/// <param name="x"></param>
		/// <param name="y"></param>
		/// <param name="w"></param>
		/// <param name="h"></param>
		/// <param name="depth"></param>
		public void UpdateRenderTileCallback(uint sessionId, uint x, uint y, uint w, uint h, uint depth, int startSample, int numSamples, int sample, int resolution)
		{
			if (State == State.Stopped) return;
			DisplayBuffer(sessionId, x, y, w, h);
		}

		/// <summary>
		/// Called when user presses the stop render button.
		/// </summary>
		override public void StopRendering()
		{
			if (RenderThread == null) return;

			lock (display_lock)
			{

				StopTheRenderer();
				Session?.Destroy();
			}

			// done, let everybody know it
			if(Settings.Verbose) sdd.WriteLine("Rendering stopped. The render window can be closed safely.");
		}

		public void PauseRendering()
		{
			State = State.Waiting;
			Session?.SetPause(true);
		}

		public void ContinueRendering()
		{
			State = State.Rendering;
			Session?.SetPause(false);
		}

		private void StopTheRenderer()
		{
			// signal that we should stop rendering.
			CancelRender = true;

			// set state to stopped
			while (State == State.Uploading)
			{
				Thread.Sleep(10);
			}
			State = State.Stopped;

			// signal our cycles session to stop rendering.
			//if (Session != null) Session.Cancel("Render stop called.\n");
			Session?.Cancel("Render stop called.\n");

			// get rid of our change queue
			Database?.Dispose();
			Database = null;

			RenderThread?.Join();
			RenderThread = null;
		}

		/// <summary>
		/// Set progress to RenderWindow, if it is not null.
		/// </summary>
		/// <param name="rw"></param>
		/// <param name="msg"></param>
		/// <param name="progress"></param>
		public void SetProgress(RenderWindow rw, string msg, float progress)
		{
			if (null != rw) rw.SetProgress(msg, progress);
		}

		/// <summary>
		/// Register the callbacks to the render engine session
		/// </summary>
		protected void SetCallbacks()
		{
			#region register callbacks with Cycles session

			Session.UpdateCallback = m_update_callback;
			Session.UpdateTileCallback = m_update_render_tile_callback;
			Session.WriteTileCallback = m_write_render_tile_callback;
			Session.TestCancelCallback = m_test_cancel_callback;
			Session.DisplayUpdateCallback = m_display_update_callback;

			#endregion
		}

		// handle material shader updates
		protected void Database_MaterialShaderChanged(object sender, MaterialShaderUpdatedEventArgs e)
		{
			RecreateMaterialShader(e.RcShader, e.CclShader);
			e.CclShader.Tag();
		}

		// handle light shader updates
		protected void Database_LightShaderChanged(object sender, LightShaderUpdatedEventArgs e)
		{
			ReCreateSimpleEmissionShader(e.RcLightShader, e.CclShader);
			e.CclShader.Tag();
		}

		protected void Database_FilmUpdateTagged(object sender, EventArgs e)
		{
			Session.Scene.Film.Update();
		}

		protected void Database_LinearWorkflowChanged(object sender, LinearWorkflowChangedEventArgs e)
		{
		}
	}

}
