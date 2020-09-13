using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MicTester : MonoBehaviour
{
    [SerializeField]
    protected Dropdown dropdown;

    AudioSource source;

    // Start is called before the first frame update
    void Start()
    {
        dropdown.ClearOptions();
        List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
        foreach(var device in Microphone.devices)
            options.Add(new Dropdown.OptionData(device));
        dropdown.options = options; //->Microphone.devices;    

        source = GetComponent<AudioSource>();
    }

    public void StartRecording()
    {
        string selected = dropdown.options[dropdown.value].text;
        Microphone.GetDeviceCaps(selected, out int minFreq, out int maxFreq);
        source.clip = Microphone.Start(selected, false, 2, maxFreq);
    }
    
    public void PlayRecording()
    {
        source.Play();
    }

}
