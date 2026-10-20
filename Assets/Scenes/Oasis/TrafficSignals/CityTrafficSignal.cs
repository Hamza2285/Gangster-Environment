using System.Collections;using UnityEngine;
[ExecuteAlways] public class CityTrafficSignal:MonoBehaviour{
public enum SignalState {Off,Red,Yellow,Green}
public SignalState state=SignalState.Red;
public bool automaticCycle=false;
[Min(.1f)]public float redSeconds=12,greenSeconds=10,yellowSeconds=3;
[SerializeField] public GameObject redLamp,yellowLamp,greenLamp;
Coroutine cycle;
void OnEnable(){Apply();if(Application.isPlaying&&automaticCycle)cycle=StartCoroutine(Cycle());}
void OnDisable(){if(cycle!=null)StopCoroutine(cycle);cycle=null;}
void OnValidate(){Apply();}
public void SetState(SignalState value){state=value;Apply();}
[ContextMenu("Lights/Red")]public void Red(){SetState(SignalState.Red);}
[ContextMenu("Lights/Yellow")]public void Yellow(){SetState(SignalState.Yellow);}
[ContextMenu("Lights/Green")]public void Green(){SetState(SignalState.Green);}
[ContextMenu("Lights/Off")]public void Off(){SetState(SignalState.Off);}
void Apply(){if(redLamp)redLamp.SetActive(state==SignalState.Red);if(yellowLamp)yellowLamp.SetActive(state==SignalState.Yellow);if(greenLamp)greenLamp.SetActive(state==SignalState.Green);}
IEnumerator Cycle(){while(true){Red();yield return new WaitForSeconds(Mathf.Max(.1f,redSeconds));Green();yield return new WaitForSeconds(Mathf.Max(.1f,greenSeconds));Yellow();yield return new WaitForSeconds(Mathf.Max(.1f,yellowSeconds));}}
}
