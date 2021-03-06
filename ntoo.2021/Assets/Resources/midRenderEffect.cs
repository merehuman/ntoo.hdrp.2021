using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using System;

[Serializable, VolumeComponentMenu("Post-processing/Custom/midRenderEffect")]
public sealed class midRenderEffect : CustomPostProcessVolumeComponent, IPostProcessComponent
{
    [Tooltip("Controls the intensity of the effect.")]
    public ClampedFloatParameter blackPixelAmount = new ClampedFloatParameter(0.75f, 0f, 1f);
    public ClampedFloatParameter pixelateAmount = new ClampedFloatParameter(0.5f, 0f, 1f);
    public ClampedFloatParameter contrastAmount = new ClampedFloatParameter(0.5f, 0f, 0.5f);

    Material m_Material;

    public bool IsActive() => m_Material != null && blackPixelAmount.value > 0f;

    // Do not forget to add this post process in the Custom Post Process Orders list (Project Settings > HDRP Default Settings).
    public override CustomPostProcessInjectionPoint injectionPoint => CustomPostProcessInjectionPoint.AfterPostProcess;

    const string kShaderName = "Hidden/Shader/midRenderEffect";

    public override void Setup()
    {
        if (Shader.Find(kShaderName) != null)
            m_Material = new Material(Shader.Find(kShaderName));
        else
            Debug.LogError($"Unable to find shader '{kShaderName}'. Post Process Volume midRenderEffect is unable to load.");
    }

    public override void Render(CommandBuffer cmd, HDCamera camera, RTHandle source, RTHandle destination)
    {
        if (m_Material == null)
            return;

        m_Material.SetFloat("_BlackPixelAmount", blackPixelAmount.value);
        m_Material.SetFloat("_Contrast", contrastAmount.value);
        m_Material.SetFloat("_PixelateAmount", 1.0f - pixelateAmount.value);
        m_Material.SetTexture("_InputTexture", source);
        HDUtils.DrawFullScreen(cmd, m_Material, destination);
    }

    public override void Cleanup()
    {
        CoreUtils.Destroy(m_Material);
    }
}
