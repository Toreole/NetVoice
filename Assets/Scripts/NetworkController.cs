using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NetworkController : MonoBehaviour
{
    [SerializeField]
    protected GameObject buttons;
    [SerializeField]
    protected GameObject serverScene;
    [SerializeField]
    protected GameObject clientScene;

    public void RunAsServer()
    {
        buttons.SetActive(false);
        serverScene.SetActive(true);
    }

    public void RunAsClient()
    {
        buttons.SetActive(false);
        clientScene.SetActive(true);
    }


}
