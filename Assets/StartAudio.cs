using System;
using System.Collections;
using Mikk.Avatar.Expression;
using UnityEngine;

public class StartAudio : MonoBehaviour
{
      

    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

       
        

    }

    private IEnumerator  startNewAudio(Playaudio ts)
    {
        yield return new WaitForSecondsRealtime(5);

        ts.Convertext("Mai aata hu");

    }
}
