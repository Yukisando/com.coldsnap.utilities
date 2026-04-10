using System;
using System.Reflection;
using UnityEngine;

public class FakeKeyboardTextTarget : MonoBehaviour
{
    [SerializeField] private FakeKeyboarder keyboarder;
    [SerializeField] private Component target;
    [SerializeField] private bool subscribeOnEnable = true;
    [SerializeField] private bool clearOnEnable = true;
    [SerializeField] private bool appendCharacters = true;
    [SerializeField] private bool notifyTargetListeners = true;

    private PropertyInfo cachedTextProperty;
    private FieldInfo cachedTextField;
    private MethodInfo cachedSetWithoutNotifyMethod;
    private Type cachedTargetType;

    private void OnEnable()
    {
        if (clearOnEnable)
        {
            ClearText();
        }

        if (subscribeOnEnable && keyboarder != null)
        {
            keyboarder.OnType += HandleTypedCharacter;
        }
    }

    private void OnDisable()
    {
        if (subscribeOnEnable && keyboarder != null)
        {
            keyboarder.OnType -= HandleTypedCharacter;
        }
    }

    public void HandleTypedCharacter(string typedCharacter)
    {
        if (string.IsNullOrEmpty(typedCharacter))
        {
            return;
        }

        string nextText = appendCharacters
            ? GetCurrentText() + typedCharacter
            : typedCharacter;

        SetText(nextText);
    }

    [ContextMenu("Clear Target Text")]
    public void ClearText()
    {
        SetText(string.Empty);
    }

    public void SetText(string value)
    {
        if (target == null)
        {
            Debug.LogWarning($"{nameof(FakeKeyboardTextTarget)} on {name} has no target assigned.", this);
            return;
        }

        CacheAccessors();

        if (!notifyTargetListeners && cachedSetWithoutNotifyMethod != null)
        {
            cachedSetWithoutNotifyMethod.Invoke(target, new object[] { value ?? string.Empty });
            return;
        }

        if (cachedTextProperty != null)
        {
            cachedTextProperty.SetValue(target, value ?? string.Empty);
            return;
        }

        if (cachedTextField != null)
        {
            cachedTextField.SetValue(target, value ?? string.Empty);
            return;
        }

        Debug.LogWarning(
            $"{nameof(FakeKeyboardTextTarget)} could not find a writable string text property or field on {target.GetType().Name}.",
            this);
    }

    private string GetCurrentText()
    {
        if (target == null)
        {
            return string.Empty;
        }

        CacheAccessors();

        if (cachedTextProperty != null)
        {
            return cachedTextProperty.GetValue(target) as string ?? string.Empty;
        }

        if (cachedTextField != null)
        {
            return cachedTextField.GetValue(target) as string ?? string.Empty;
        }

        return string.Empty;
    }

    private void CacheAccessors()
    {
        if (target == null)
        {
            cachedTargetType = null;
            cachedTextProperty = null;
            cachedTextField = null;
            cachedSetWithoutNotifyMethod = null;
            return;
        }

        Type targetType = target.GetType();
        if (cachedTargetType == targetType)
        {
            return;
        }

        cachedTargetType = targetType;
        cachedTextProperty = targetType.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        if (cachedTextProperty != null)
        {
            bool validProperty = cachedTextProperty.CanRead
                && cachedTextProperty.CanWrite
                && cachedTextProperty.PropertyType == typeof(string);
            if (!validProperty)
            {
                cachedTextProperty = null;
            }
        }

        cachedTextField = targetType.GetField("text", BindingFlags.Instance | BindingFlags.Public);
        if (cachedTextField != null && cachedTextField.FieldType != typeof(string))
        {
            cachedTextField = null;
        }

        cachedSetWithoutNotifyMethod = targetType.GetMethod(
            "SetTextWithoutNotify",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(string) },
            null);
    }
}