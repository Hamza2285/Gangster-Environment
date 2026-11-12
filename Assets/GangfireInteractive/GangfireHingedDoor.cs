using UnityEngine;
[DisallowMultipleComponent]
public sealed class GangfireHingedDoor : MonoBehaviour
{
 public bool isOpen=true;
 public float openingAngle=72f;
 public float speedDegrees=150f;
 [SerializeField] Quaternion closedRotation=Quaternion.identity;
 [SerializeField] Quaternion openRotation=Quaternion.identity;
 public void Configure(Quaternion closed,Quaternion open,bool startOpen){closedRotation=closed;openRotation=open;openingAngle=Mathf.DeltaAngle(closed.eulerAngles.y,open.eulerAngles.y);isOpen=startOpen;transform.localRotation=isOpen?openRotation:closedRotation;}
 [ContextMenu("Open")] public void Open(){isOpen=true;if(!Application.isPlaying)transform.localRotation=openRotation;}
 [ContextMenu("Close")] public void Close(){isOpen=false;if(!Application.isPlaying)transform.localRotation=closedRotation;}
 [ContextMenu("Toggle")] public void Toggle(){if(isOpen)Close();else Open();}
 void OnValidate(){openRotation=closedRotation*Quaternion.Euler(0,openingAngle,0);} void Update(){var target=isOpen?openRotation:closedRotation;if(Quaternion.Angle(transform.localRotation,target)>.01f)transform.localRotation=Quaternion.RotateTowards(transform.localRotation,target,speedDegrees*Time.deltaTime);}
}

