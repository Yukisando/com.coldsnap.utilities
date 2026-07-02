using System;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;

public static class SliderExtensions
{
    /// <summary>
    /// Keeps a TMP_Text in sync with this slider's value: writes the current value immediately,
    /// then updates it on every onValueChanged. Pass a custom formatter for non-numeric display
    /// (percentages, labels, etc). Save the returned UnityAction and pass it to
    /// UnbindTextFromValue when the target text or slider is destroyed to avoid a leaked listener.
    /// </summary>
    public static UnityAction<float> BindTextToValue(
        this Slider slider,
        TMP_Text text,
        string format = "0.##",
        Func<float, string> formatter = null)
    {
        if (slider == null)
        {
            throw new ArgumentNullException(nameof(slider));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        UnityAction<float> handler = value =>
        {
            text.text = formatter != null ? formatter(value) : value.ToString(format);
        };

        handler(slider.value);
        slider.onValueChanged.AddListener(handler);
        return handler;
    }

    /// <summary>
    /// Removes a listener previously returned by BindTextToValue.
    /// </summary>
    public static void UnbindTextFromValue(this Slider slider, UnityAction<float> handler)
    {
        if (slider == null || handler == null)
        {
            return;
        }

        slider.onValueChanged.RemoveListener(handler);
    }
}
