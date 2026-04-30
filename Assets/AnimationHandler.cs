using System;
using System.Collections;
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using Mikk.Avatar.Expression;
using OpenAI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;


namespace Mikk.Avatar.Expression
{

    

    public class AnimationHandler : NetworkBehaviour
    {

       

        private void Awake()
        {
            gameObject.SetActive(false);
        }



        public override void OnStartClient()
        {
            base.OnStartClient();


            if (IsOwner)
            {
                gameObject.SetActive(true);
            }

        }




       

       





        void sendMikkMsg(string v)
        {

         transform.parent.GetComponent<AvatarChatManager>().ProcessChatMessage(v);



        }


        public void MuteAudioOnClient(string fake)
        {


            transform.parent.GetChild(7).GetComponent<VoicePipeline>().Mute();


        }



        public void UnmuteAudioOnClient(string fake)
        {


            transform.parent.GetChild(7).GetComponent<VoicePipeline>().Unmute();


        }








    }

}