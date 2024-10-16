/**
Copyright 2014-2024 Robert McNeel and Associates

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

namespace RhinoCyclesCore.Settings
{
	public static class DefaultEngineSettings
	{
		static public bool Verbose => false;

		static public float SpotLightFactor => 40.0f;
		static public float PointLightFactor => 40.0f;
		static public float SunLightFactor => 3.2f;
		static public float LinearLightFactor => 10.0f;
		static public float AreaLightFactor => 17.2f;
		static public float PolishFactor => 0.09f;

		static public int ThrottleMs => 100;
		static public int Threads => Math.Max(1, Utilities.GetSystemProcessorCount() - 2);

		static public bool ExperimentalCpuInMulti => false;

		static public float BumpDistance => 1.0f;
		static public float NormalStrengthFactor => 1.0f;
		static public float BumpStrengthFactor => 1.0f;

		static public string SelectedDeviceStr => "-1";
		static public bool AllowSelectedDeviceOverride => false;

		static public bool UseStartResolution => RenderEngine.DefaultPixelSizeBasedOnMonitorResolution > 1;
		static public int StartResolution => RenderEngine.DefaultPixelSizeBasedOnMonitorResolution > 1 ? 128 : int.MaxValue;

		static public int PixelSize => Math.Max(1, RenderEngine.DefaultPixelSizeBasedOnMonitorResolution);
		static public float OldDpiScale => Math.Max(1.0f, RenderEngine.DefaultPixelSizeBasedOnMonitorResolution);

		static public int TileX => 128;
		static public int TileY => 128;

		static public int MaxBounce => 32;

		static public bool NoCaustics => false;
		static public bool CausticsReflective => true;
		static public bool CausticsRefractive => true;

		static public int MaxDiffuseBounce => 4;
		static public int MaxGlossyBounce => 16;
		static public int MaxTransmissionBounce => 32;

		static public int MaxVolumeBounce => 32;

		static public int AaSamples => 32;

		static public int DiffuseSamples => 32;
		static public int GlossySamples => 32;
		static public int TransmissionSamples => 32;

		static public int AoBounces => 0;
		static public float AoFactor => 0.0f;
		static public float AoDistance => float.MaxValue;
		static public float AoAdditiveFactor => 0.0f;

		static public int MeshLightSamples => 32;
		static public int SubSurfaceSamples => 32;
		static public int VolumeSamples => 32;

		static public int Samples => 1000;
		static public bool UseDocumentSamples => false;
		/// <summary>
		/// Texture bake quality 0-3
		///
		/// 0 = low
		/// 1 = standard
		/// 2 = high
		/// 3 = ultra
		/// 4 = disabled
		/// </summary>
		static public int TextureBakeQuality => 0;
		static public int Seed => 128;

		static public float FilterGlossy => 0.5f;

		static public float SampleClampDirect => 3.0f;
		static public float SampleClampIndirect => 3.0f;
		static public float LightSamplingThreshold => 0.05f;

		static public bool UseDirectLight => true;
		static public bool UseIndirectLight => true;

		static public int Blades => 0;
		static public float BladesRotation => 0.0f;
		static public float ApertureRatio => 1.0f;
		static public float ApertureFactor => 0.1f;

		static public float SensorWidth => 32.0f;
		static public float SensorHeight => 18.0f;

		static public int TransparentMaxBounce => 32;

		static public int SssMethod => 44;

		static public bool ShowMaxPasses => true;
		static public int OpenClDeviceType => 4;
		static public int OpenClKernelType => 0;
		static public bool CPUSplitKernel => true;
		static public bool OpenClSingleProgram => true;
		static public bool NoShadows => false;
		static public bool SaveDebugImages => false;
		static public bool DebugSimpleShaders => false;
		static public bool DebugNoOverrideTileSize => false;
		static public bool FlushAtEndOfCreateWorld => false;
		static public int PreviewSamples => 150;

		static public bool DumpMaterialShaderGraph => false;
		static public bool DumpEnvironmentShaderGraph => false;
		static public bool StartGpuKernelCompiler => true;
		static public bool VerboseLogging => false;
		static public int RetentionDays => 3;
		static public int TriggerPostEffectsSample => 5;

		static public bool UseLightTree => true;
		static public bool UseAdaptiveSampling	=> true;
		static public int AdaptiveMinSamples => 16;
		static public float AdaptiveThreshold => 0.01f;
	}
}
