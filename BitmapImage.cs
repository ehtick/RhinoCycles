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
using System.Drawing.Imaging;
using System.Linq;
using Rhino.Display;
using Rhino.Render;
using Rhino.Runtime.InteropWrappers;

namespace RhinoCyclesCore
{
	public class BitmapImage<T> : IDisposable
	{
		internal object Original;   //TextureEvaluator.FloatByte or TextureEvaluator.FloatArray
		internal object Corrected;  //TextureEvaluator.FloatByte or TextureEvaluator.FloatArray

		protected int W;
		protected int H;

		public uint Id { get; }

		public bool IsLinear { get; }

		internal BitmapImage() { }

		protected static int ColorClamp(int ch)
		{
			if (ch < 0) return 0;
			return ch > 255 ? 255 : ch;
		}

		internal bool GammaApplied { get; set; }

		internal float GammaValueApplied { get; set; }

		public BitmapImage(uint id, object data, int w, int h, bool linear)
		{
			Id = id;

			W = w;
			H = h;

			IsLinear = linear;

			Original = data;
		}

		virtual public void ApplyGamma(float gamma)
		{
		}

		public object Data => IsLinear || !GammaApplied ? Original : Corrected;

		public void SaveBitmaps()
		{
			SavePixels(Original, $"{Id}_original");
			if (!IsLinear && Corrected != null)
			{
				SavePixels(Corrected, $"{Id}_corrected");
			}
		}

		protected virtual void SavePixels(object pixels, string name) {}

		public void Dispose()
		{
			Original = null;
			Corrected = null;
		}
	}

	public class ByteBitmap : BitmapImage<byte>
	{

		public ByteBitmap(uint id, TextureEvaluator.ByteArray data, int w, int h, bool linear) : base(id, data, w, h, linear)
		{ }

		override public void ApplyGamma(float gamma)
		{
			if (!IsLinear && Math.Abs(gamma - 1.0f) > float.Epsilon)
			{
				if (null == Corrected || Math.Abs(gamma - GammaValueApplied) > float.Epsilon)
				{
					if (Corrected == null)
					{
						Corrected = new TextureEvaluator.ByteArray(Original as TextureEvaluator.ByteArray);
					}
					else
					{
						(Original as TextureEvaluator.ByteArray).CopyTo(Corrected as TextureEvaluator.ByteArray);
					}

					ccl.CSycles.apply_gamma_to_byte_buffer((Corrected as TextureEvaluator.ByteArray).c_array(), W*H*4, gamma);
				}
				GammaApplied = true;
				GammaValueApplied = gamma;
			}
			else
			{
				GammaApplied = false;
				Corrected = null;
			}
		}

		protected override void SavePixels(object oPixels, string name)
		{
			var pixels = (oPixels as TextureEvaluator.ByteArray).ToArray();

			using (var rw = RenderWindow.Create(new Size(W, H)))
			{
				using (var ch = rw.OpenChannel(RenderWindow.StandardChannels.RGBA))
				{
					for (var x = 0; x < W; x++)
					{
						for (var y = 0; y < H; y++)
						{
							var i = y*W*4 + x*4;
							ch.SetValue(x, y, Color4f.FromArgb(pixels[i + 3]/255.0f, pixels[i]/255.0f, pixels[i + 1]/255.0f, pixels[i + 2]/255.0f));
						}
					}
				}
				var tmpfhdr = RenderEngine.TempPathForFile($"byte_{name}.exr");
				rw.SaveRenderImageAs(tmpfhdr, true);
			}
		}
	}

	public class FloatBitmap : BitmapImage<float>
	{
		public FloatBitmap(uint id, TextureEvaluator.FloatArray data, int w, int h, bool linear) : base(id, data, w, h, linear)
		{ }

		override public void ApplyGamma(float gamma)
		{
			if (!IsLinear && Math.Abs(gamma - 1.0f) > float.Epsilon)
			{
				if (null == Corrected || Math.Abs(gamma - GammaValueApplied) > float.Epsilon)
				{
					if (Corrected == null)
					{
						Corrected = new TextureEvaluator.FloatArray(Original as TextureEvaluator.FloatArray);
					}
					else
					{
						(Original as TextureEvaluator.FloatArray).CopyTo(Corrected as TextureEvaluator.FloatArray);
					}

					ccl.CSycles.apply_gamma_to_float_buffer((Corrected as TextureEvaluator.FloatArray).c_array(), W*H*4*4, gamma);

				}
				GammaApplied = true;
				GammaValueApplied = gamma;
			}
			else
			{
				GammaApplied = false;
				Corrected = null;
			}
		}

		protected override void SavePixels(object oPixels, string name)
		{
			var pixels = (oPixels as TextureEvaluator.FloatArray).ToArray();

			using (var rw = RenderWindow.Create(new Size(W, H)))
			{
				using (var ch = rw.OpenChannel(RenderWindow.StandardChannels.RGBA))
				{
					for (var x = 0; x < W; x++)
					{
						for (var y = 0; y < H; y++)
						{
							var i = y*W*4 + x*4;
							ch.SetValue(x, y, Color4f.FromArgb(pixels[i+3], pixels[i], pixels[i+1], pixels[i+2]));
						}
					}
				}
				var tmpfhdr = RenderEngine.TempPathForFile($"float_{ name}.exr");
				rw.SaveRenderImageAs(tmpfhdr, true);
			}
		}

	}
}
