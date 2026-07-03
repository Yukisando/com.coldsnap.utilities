using TMPro;
using UnityEngine;

public class SliderValueTextBinder : MonoBehaviour
{
    [SerializeField] private TMP_Text text;
    [SerializeField] private string format = "0.##";
    [SerializeField] private string prefix = "";
    [SerializeField] private string suffix = "";

    private void Reset()
    {
        if (text == null)
        {
            text = GetComponent<TMP_Text>();
        }
    }

    public void SetValue(float value)
    {
        if (text == null)
        {
            Debug.LogWarning($"{nameof(SliderValueTextBinder)} on {name} has no text assigned.", this);
            return;
        }

        text.text = prefix + value.ToString(format) + suffix;
    }
}
