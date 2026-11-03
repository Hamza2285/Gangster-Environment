using UnityEngine;
public static partial class MiamiClothingBuild {
static void GunBoard(G g,Vector3 c,Vector3 size){int start=g.p.Count;g.Box(c,size,1);for(int i=start;i<start+6;i++){var uv=g.uv[i];uv.x+=32;g.uv[i]=uv;}}
static void GunShell(G g,float w,float d,float h){float wallH=h-.20f,z=d/2,sideDepth=d+.02f;GunStoneFace(g,new Vector3(0,wallH/2,-d/2),w,wallH,Quaternion.Euler(0,180,0),12);GunStoneFace(g,new Vector3(-w/2,wallH/2,.01f),sideDepth,wallH,Quaternion.Euler(0,-90,0),12);GunStoneFace(g,new Vector3(w/2,wallH/2,.01f),sideDepth,wallH,Quaternion.Euler(0,90,0),12);GunStoneFace(g,new Vector3(0,wallH/2,z-1),w-.8f,wallH,Quaternion.identity,12);
foreach(float sign in new[]{-1f,1f}){GunStoneFace(g,new Vector3(sign*(w/2-.2f),wallH/2,z+.02f),.40f,wallH,Quaternion.identity,12);GunStoneFace(g,new Vector3(sign*(w/2-.4f),wallH/2,z-.49f),1.02f,wallH,Quaternion.Euler(0,sign>0?-90:90,0),12);}
g.Box(new Vector3(0,h-.10f,.01f),new Vector3(w+.04f,.20f,d+.06f),7);
}
static void GunGrooves(G g,float x,float y,float z,float width,float height,int count){g.Box(new Vector3(x,y,z),new Vector3(width,height,.12f),1);for(int i=0;i<count;i++){float yy=y-height/2+(i+.5f)*height/count;g.Box(new Vector3(x,yy,z+.072f),new Vector3(width-.08f,.045f,.015f),7);g.Box(new Vector3(x,yy,z+.092f),new Vector3(width-.12f,.012f,.012f),6,5);}}
static void GunSideArt(G g,float w,float d,float h){int count=3;float span=d-.12f,part=span/count,ah=h-1.14f;foreach(float sign in new[]{-1f,1f}){var q=Quaternion.Euler(0,sign<0?-90:90,0);for(int i=0;i<count;i++){float z=d/2-.06f-(i+.5f)*part;g.Panel(new Vector3(sign*(w/2+.04f),.24f+ah/2,z),part-.025f,ah,GunTile(new[]{2,0,1}[i]),4,q);}}}
}

