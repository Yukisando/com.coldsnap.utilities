using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class FakeKeyboarder : MonoBehaviour
{
    [Serializable]
    public class StringEvent : UnityEvent<string>
    {
    }

    [SerializeField] private string input = "abs";
    [SerializeField] private float inputDelay = 0.05f;
    [SerializeField] private bool playOnEnable;
    [SerializeField] private bool restartIfAlreadyTyping = true;
    [SerializeField] private StringEvent onType = new StringEvent();

    public event Action<string> OnType;

    private Coroutine typingCoroutine;

    private void OnEnable()
    {
        if (playOnEnable)
        {
            StartTyping();
        }
    }

    private void OnDisable()
    {
        StopTyping();
    }

    [ContextMenu("Start Typing")]
    public void StartTyping()
    {
        if (typingCoroutine != null)
        {
            if (!restartIfAlreadyTyping)
            {
                return;
            }

            StopTyping();
        }

        typingCoroutine = StartCoroutine(TypeRoutine());
    }

    [ContextMenu("Stop Typing")]
    public void StopTyping()
    {
        if (typingCoroutine == null)
        {
            return;
        }

        StopCoroutine(typingCoroutine);
        typingCoroutine = null;
    }

    public void SetInput(string value)
    {
        input = value ?? string.Empty;
    }

    private IEnumerator TypeRoutine()
    {
        if (string.IsNullOrEmpty(input))
        {
            typingCoroutine = null;
            yield break;
        }

        float clampedDelay = Mathf.Max(0f, inputDelay);

        foreach (char character in input)
        {
            if (clampedDelay > 0f)
            {
                yield return new WaitForSeconds(clampedDelay);
            }

            string typedCharacter = character.ToString();
            OnType?.Invoke(typedCharacter);
            onType.Invoke(typedCharacter);
        }

        typingCoroutine = null;
    }
}