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

using ccl;
using RhinoCyclesCore.Core;

namespace RhinoCyclesCore.Shaders
{
	public abstract class RhinoShader
	{
		protected Shader m_shader;
		protected CyclesShader m_original;
		protected CyclesBackground m_original_background;
		protected CyclesLight m_original_light;

		protected Client m_client;

		private void InitShader(string name, Shader existing, Shader.ShaderType shaderType, bool recreate)
		{
			if (existing != null)
			{
				m_shader = existing;
				if(recreate) m_shader.Recreate();
			}
			else
			{
				m_shader = new Shader(m_client, shaderType)
				{
					UseMis = true,
					UseTransparentShadow = true,
					HeterogeneousVolume = false,
					Name = name,
					Verbose = shaderType == Shader.ShaderType.Material ? RcCore.It.AllSettings.Verbose : false
				};
			}

		}
		protected RhinoShader(Client client, CyclesShader intermediate, string name, Shader existing, bool recreate)
		{
			m_client = client;
			m_original = intermediate;
			if (m_original.Front != null) m_original.Front.Gamma = m_original.Gamma;
			if (m_original.Back != null) m_original.Back.Gamma = m_original.Gamma;
			InitShader(name, existing, Shader.ShaderType.Material, recreate);

		}

		protected RhinoShader(Client client, CyclesBackground intermediateBackground, string name, Shader existing, bool recreate)
		{
			m_client = client;
			m_original_background = intermediateBackground;
			InitShader(name, existing, Shader.ShaderType.World, recreate);
		}

		protected RhinoShader(Client client, CyclesLight intermediateLight, string name, Shader existing, bool recreate)
		{
			m_client = client;
			m_original_light = intermediateLight;
			InitShader(name, existing, Shader.ShaderType.Material, recreate);
		}

		public void Reset()
		{
			m_shader?.Recreate();
		}

		public static RhinoShader CreateRhinoMaterialShader(Client client, CyclesShader intermediate)
		{
			RhinoShader theShader = new RhinoFullNxt(client, intermediate);

			return theShader;
		}

		public static RhinoShader RecreateRhinoMaterialShader(Client client, CyclesShader intermediate, Shader existing)
		{
			RhinoShader theShader = new RhinoFullNxt(client, intermediate, existing);

			return theShader;
		}

		public static RhinoShader CreateRhinoBackgroundShader(Client client, CyclesBackground intermediateBackground, Shader existingShader)
		{
			RhinoShader theShader = new RhinoBackground(client, intermediateBackground, existingShader);
			return theShader;
		}

		public static RhinoShader CreateRhinoLightShader(Client client, CyclesLight intermediateLight, Shader existingShader)
		{
			RhinoShader shader = new RhinoLight(client, intermediateLight, existingShader);
			return shader;
		}

		/// <summary>
		/// Get the ccl.Shader representing this
		/// </summary>
		/// <returns></returns>
		public abstract Shader GetShader();
	}
}
