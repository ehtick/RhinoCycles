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

using System.Drawing;
using System;
using System.Runtime.InteropServices;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;
using ccl.ShaderNodes.Sockets;

namespace RhinoCyclesCore.Materials
{
	[Guid("3CEC0E39-8A13-4E73-8D0C-F1F1DF730C35")]
	[CustomRenderContent(IsPrivate=true)]
	public class GlassMaterial : RenderMaterial, ICyclesMaterial
	{
		public override string TypeName => "Cycles Glass";

		public override string TypeDescription => "Cycles Glass Material";

		public float Gamma { get; set; }

		public ShaderBody.CyclesMaterial MaterialType => ShaderBody.CyclesMaterial.Glass;

		private Color4f Color { get; set; }
		private float Frost { get; set; }
		private float Ior { get; set; }

		public GlassMaterial()
		{
			Color = Color4f.White;
			Fields.Add("glass_color", Color, "Glass Color");
			Frost = 0.0f;
			Fields.Add("frost-amount", Frost, "Frost");
			Ior = 1.45f;
			Fields.Add("ior", Ior, "IOR");
		}

		public void BakeParameters()
		{
			Color4f col;
			if (Fields.TryGetValue("glass_color", out col))
			{
				Color = col;
			}
			float val;
			if (Fields.TryGetValue("frost-amount", out val))
			{
				Frost = val;
			}
			if (Fields.TryGetValue("ior", out val))
			{
				Ior = val;
			}
		}

		protected override void OnAddUserInterfaceSections()
		{
			AddAutomaticUserInterfaceSection("Parameters", 0);
		}

		public override void SimulateMaterial(ref Material simulatedMaterial, bool forDataOnly)
		{
			base.SimulateMaterial(ref simulatedMaterial, forDataOnly);

			simulatedMaterial.Reflectivity = 1.0;
			simulatedMaterial.Transparency = 1.0;
			simulatedMaterial.FresnelReflections = true;
			simulatedMaterial.DiffuseColor = System.Drawing.Color.Black;

			Color4f color;
			if (Fields.TryGetValue("glass_color", out color))
				simulatedMaterial.TransparentColor = color.AsSystemColor();

			float f;
			if (Fields.TryGetValue("frost-amount", out f))
			{
				simulatedMaterial.ReflectionGlossiness = f;
				simulatedMaterial.RefractionGlossiness = f;
			}
			if (Fields.TryGetValue("ior", out f))
			{
				simulatedMaterial.IndexOfRefraction = f;
			}

			simulatedMaterial.Name = Name;


		}




		public string MaterialXml
		{
			get
			{

				float frost = (float)Math.Pow(Frost, 2);

				Color4f color = Color4f.ApplyGamma(Color, Gamma);

				return string.Format(
					ccl.Utilities.Instance.NumberFormatInfo,
					"<transparent_bsdf color=\"{0} {1} {2}\" name=\"transp\" />" +
					"<glass_bsdf color=\"{0} {1} {2}\" roughness=\"{3}\" ior=\"{4}\" name=\"glass\" />" +
					"<light_path name=\"lp\" />" +
					"<math type=\"Maximum\" name=\"max\" />" +
					"<mix_closure name=\"mix\" />" +

					"<connect from=\"transp bsdf\" to=\"mix closure2\" />" +
					"<connect from=\"glass bsdf\" to=\"mix closure1\" />" +
					"<connect from=\"lp isshadowray\" to=\"max value1\" />" +
					"<connect from=\"lp isreflectionray\" to=\"max value2\" />" +
					"<connect from=\"max value\" to=\"mix fac\" />" +
					"<connect from=\"mix closure\" to=\"output surface\" />" +
					"",
					
					color.R, color.G, color.B,
					frost,
					Ior);
			}
		}
		public bool GetShader(ccl.Shader sh, bool finalize)
		{
			try
			{
				ccl.Shader.ShaderFromXml(sh, MaterialXml, finalize);
			}
			catch (Exception)
			{
				return false;
			}
			return true;
		}
		public ClosureSocket GetClosureSocket(ccl.Shader sh)
		{
			return sh.Output.ins.Surface.ConnectionFrom as ClosureSocket;
		}
	}
}
