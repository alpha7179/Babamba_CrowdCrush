using NUnit.Framework.Interfaces;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.UI;

public class LocalizationTest : MonoBehaviour
{


    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)) UpdateLocales(0);      // 영어
        if (Input.GetKeyDown(KeyCode.K)) UpdateLocales(1);      // 한국어


    }

    public void UpdateLocales(int index)
    {
        // LocalizationSettings.SelectedLocale : 현재 언어를 설정하거나 불러오는 프로퍼티
        LocalizationSettings.SelectedLocale = LocalizationSettings.AvailableLocales.Locales[index];
    }

}