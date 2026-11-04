using UnityEngine;
public static partial class MiamiClothingBuild{
static void GunGlow(G g,Vector3 c,float w,float h,Quaternion q){g.Panel(c,w,h,new Rect(1631.8f/4096f,16/3072f,.4f/4096f,128/3072f),10,q);}
static Rect GunStoneRect=>new Rect(1715/4096f,19/3072f,122/4096f,122/3072f);
static void BakeGunCladding(Texture2D atlas){for(int y=0;y<512;y++)for(int x=0;x<128;x++){float xx=x,yy=y%128;float n=.94f+.055f*Mathf.PerlinNoise(xx*.4f,yy*.4f)+.035f*Mathf.PerlinNoise(xx*.12f,yy*.12f);if(x<2||yy<2)n*=.58f;atlas.SetPixel(1712+x,16+y,new Color(.265f,.273f,.285f)*n);}}
static void GunStoneFace(G g,Vector3 c,float width,float height,Quaternion rotation,int kind){int nx=Mathf.Max(1,Mathf.CeilToInt(width/2)),ny=Mathf.Max(1,Mathf.CeilToInt(height/2));float sx=width/nx,sy=height/ny;for(int x=0;x<nx;x++)for(int y=0;y<ny;y++)g.Panel(c+rotation*new Vector3(-width/2+(x+.5f)*sx,-height/2+(y+.5f)*sy,0),sx,sy,GunStoneRect,kind,rotation);}
static void GunStoneBox(G g,Vector3 c,Vector3 size,int kind=12){GunStoneFace(g,c+Vector3.forward*size.z/2,size.x,size.y,Quaternion.identity,kind);GunStoneFace(g,c-Vector3.forward*size.z/2,size.x,size.y,Quaternion.Euler(0,180,0),kind);GunStoneFace(g,c+Vector3.right*size.x/2,size.z,size.y,Quaternion.Euler(0,90,0),kind);GunStoneFace(g,c-Vector3.right*size.x/2,size.z,size.y,Quaternion.Euler(0,-90,0),kind);GunStoneFace(g,c+Vector3.up*size.y/2,size.x,size.z,Quaternion.Euler(-90,0,0),kind);GunStoneFace(g,c-Vector3.up*size.y/2,size.x,size.z,Quaternion.Euler(90,0,0),kind);}
static void GunWash(G g,Vector3 c,float width,float height,Quaternion q){g.Panel(c,width,height,GunStoneRect,14,q);}
}

