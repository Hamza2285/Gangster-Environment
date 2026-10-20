using System;using System.IO;using System.Linq;using System.Collections.Generic;using UnityEngine;using UnityEditor;
public static partial class StackHouse3DSigns{
static bool taxiFarDetail;
static Vector2 taxiYellow,taxiBlack,taxiSteel,taxiWhite,taxiGlass;
static void TaxiText(G g,string text,float x,float y,float z,float width,float height,float glow,bool depth){var word=JsonUtility.FromJson<LetterContours>(File.ReadAllText("Assets/Scenes/Oasis/TaxiStand/Taxi Letter Contours.json")).words.Single(w=>w.text==text);var points=word.loops.SelectMany(l=>l.points).ToArray();float minx=points.Min(v=>v.x),maxx=points.Max(v=>v.x),miny=points.Min(v=>v.y),maxy=points.Max(v=>v.y);var loops=word.loops.Select(l=>l.points.Select(v=>new Vector2(x+((v.x-minx)/(maxx-minx)-.5f)*width,y+(.5f-(v.y-miny)/(maxy-miny))*height)).ToArray()).ToList();if(!depth)loops=loops.Select(l=>l.Select(v=>new Vector2(Mathf.Round(v.x/(height/(taxiFarDetail?24:40)))*(height/(taxiFarDetail?24:40)),Mathf.Round(v.y/(height/(taxiFarDetail?12:20)))*(height/(taxiFarDetail?12:20)))).ToArray()).ToList();surfaceType=0;if(text=="Taxi Stand"){foreach(bool yellow in new[]{true,false})FillContours(g,loops.Where(l=>(l.Average(v=>v.x)<x-width*.1f)==yellow).ToList(),z,yellow?taxiYellow:taxiWhite,glow);}else if(text=="TAXI BOOKING"){FillContours(g,loops.Where(l=>l.Average(v=>v.x)<x-width*.22f).ToList(),z,taxiWhite,glow);FillContours(g,loops.Where(l=>l.Average(v=>v.x)>=x-width*.22f).ToList(),z,taxiYellow,glow);}else FillContours(g,loops,z,taxiYellow,glow);if(depth){surfaceType=1;foreach(var loop in loops)for(int i=0;i<loop.Length;i++){var a=loop[i];var b=loop[(i+1)%loop.Length];g.Quad(new Vector3(a.x,a.y,z),new Vector3(a.x,a.y,z-.14f),new Vector3(b.x,b.y,z-.14f),new Vector3(b.x,b.y,z),taxiSteel,taxiSteel,taxiSteel,taxiSteel);}}}
static void TaxiCheckers(G g,float x,float y,float z,float width,float height){surfaceType=0;float w=width/6;for(int row=0;row<2;row++)for(int col=0;col<6;col++)if((row+col)%2==0)g.Box(new Vector3(x-width*.5f+(col+.5f)*w,y-height*.5f+(row+.5f)*height*.5f,z),new Vector3(w*.98f,height*.49f,.035f),taxiYellow,.2f);}
static void TaxiIcon(G g,float x,float y,float z,float scale){surfaceType=0;var poly=new[]{new Vector2(-.65f,-.36f),new Vector2(-.65f,.18f),new Vector2(-.44f,.30f),new Vector2(-.30f,.69f),new Vector2(.30f,.69f),new Vector2(.44f,.30f),new Vector2(.65f,.18f),new Vector2(.65f,-.36f)};FillContours(g,new List<Vector2[]>{poly.Select(v=>new Vector2(x+v.x*scale,y+v.y*scale)).ToArray()},z,taxiYellow,.45f);g.Box(new Vector3(x,y+.47f*scale,z+.015f),new Vector3(.61f*scale,.23f*scale,.03f),taxiBlack);foreach(float side in new[]{-1f,1f}){g.Box(new Vector3(x+side*.42f*scale,y-.02f*scale,z+.025f),new Vector3(.22f*scale,.12f*scale,.035f),taxiBlack);g.Box(new Vector3(x+side*.45f*scale,y-.40f*scale,z),new Vector3(.19f*scale,.24f*scale,.05f),taxiYellow);}g.Box(new Vector3(x,y+.78f*scale,z),new Vector3(.37f*scale,.13f*scale,.06f),taxiYellow);}
public static Mesh[] BuildTaxiStandMeshes(bool detail){taxiFarDetail=!detail;var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/Scenes/Oasis/FinalEnvironmentRoads/RoadDetailAtlas/City PNG Parking Cafe.mat");atlas=new Texture2D(2,2);atlas.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap"))));taxiYellow=ColorUV(new Color(1,.72f,.025f));taxiBlack=ColorUV(new Color(.045f,.05f,.055f));taxiSteel=ColorUV(new Color(.28f,.30f,.32f));taxiWhite=ColorUV(new Color(.93f,.92f,.87f));taxiGlass=ColorUV(new Color(.16f,.25f,.31f));var structure=new G();var booth=new G();var signs=new G();var ground=new G();surfaceType=2;
structure.Box(new Vector3(0,4.58f,1.8f),new Vector3(12.4f,.34f,12.5f),taxiSteel);structure.Box(new Vector3(0,4.92f,1.8f),new Vector3(12.65f,.44f,12.75f),taxiBlack);structure.Box(new Vector3(0,5.16f,1.8f),new Vector3(12.8f,.075f,12.9f),taxiSteel);
foreach(float x in new[]{-5.65f,5.65f})foreach(float z in new[]{-3.7f,6.95f}){structure.Box(new Vector3(x,2.24f,z),new Vector3(.30f,4.48f,.30f),taxiBlack);structure.Box(new Vector3(x,.19f,z),new Vector3(.60f,.38f,.60f),taxiSteel);structure.Box(new Vector3(x,.75f,z),new Vector3(.32f,.33f,.32f),taxiYellow);if(detail){var line=new List<Vector3>{new Vector3(x,3.72f,z),new Vector3(x-Mathf.Sign(x)*.74f,4.42f,z)};Tube(structure,line,.065f,taxiYellow,0,4);}}
if(detail){foreach(float z in new[]{-2.7f,0,2.7f,5.4f})structure.Box(new Vector3(0,4.37f,z),new Vector3(11.8f,.14f,.12f),taxiBlack);foreach(float x in new[]{-4,0,4})structure.Box(new Vector3(x,4.39f,1.8f),new Vector3(.11f,.14f,12.1f),taxiBlack);}
surfaceType=0;foreach(float x in new[]{-6.31f,6.31f})structure.Box(new Vector3(x,4.93f,1.8f),new Vector3(.035f,.10f,12.4f),taxiYellow,1.35f);foreach(float z in new[]{-4.56f,8.16f})structure.Box(new Vector3(0,4.77f,z),new Vector3(12.5f,.09f,.035f),taxiYellow,1.35f);foreach(float x in new[]{-4f,0,4f})foreach(float z in new[]{-1.5f,3.7f}){surfaceType=2;structure.Box(new Vector3(x,4.35f,z),new Vector3(.70f,.07f,.40f),taxiBlack);surfaceType=0;structure.Box(new Vector3(x,4.30f,z),new Vector3(.49f,.025f,.25f),taxiWhite,2.2f);}
// Booking booth kept behind the parking envelope, with a front service window.
surfaceType=2;Vector3 bc=new Vector3(3.95f,0,-6.5f);booth.Box(bc+new Vector3(0,.13f,0),new Vector3(3.5f,.26f,3.3f),taxiSteel);foreach(float z in new[]{-1.43f,1.43f})booth.Box(bc+new Vector3(0,.76f,z),new Vector3(3.35f,1.25f,.13f),taxiBlack);foreach(float x in new[]{-1.6f,1.6f})booth.Box(bc+new Vector3(x,.76f,0),new Vector3(.13f,1.25f,2.9f),taxiBlack);
surfaceType=1;foreach(float z in new[]{-1.44f,1.44f})booth.Box(bc+new Vector3(0,2.03f,z),new Vector3(3.18f,1.35f,.04f),taxiGlass);foreach(float x in new[]{-1.6f,1.6f})booth.Box(bc+new Vector3(x,2.03f,0),new Vector3(.04f,1.35f,2.83f),taxiGlass);
surfaceType=2;foreach(float x in new[]{-1.65f,0,1.65f})foreach(float z in new[]{-1.48f,1.48f})booth.Box(bc+new Vector3(x,1.84f,z),new Vector3(.07f,2.0f,.07f),taxiSteel);booth.Box(bc+new Vector3(0,2.91f,0),new Vector3(3.65f,.35f,3.5f),taxiBlack);booth.Box(bc+new Vector3(0,3.12f,0),new Vector3(3.75f,.07f,3.6f),taxiSteel);booth.Box(bc+new Vector3(0,1.32f,1.54f),new Vector3(3.45f,.12f,.30f),taxiSteel);surfaceType=0;booth.Box(bc+new Vector3(0,1.10f,1.51f),new Vector3(3.33f,.18f,.025f),taxiYellow,.2f);TaxiText(booth,"TAXI BOOKING",bc.x,2.91f,bc.z+1.77f,3.20f,.25f,.55f,false);booth.Box(bc+new Vector3(0,2.71f,1.77f),new Vector3(3.35f,.025f,.025f),taxiWhite,1.1f);
// Raised front fascia, checker ends and taxi icon.
surfaceType=2;signs.Box(new Vector3(0,5.13f,8.19f),new Vector3(10.7f,1.55f,.20f),taxiBlack);surfaceType=0;signs.Box(new Vector3(0,4.38f,8.31f),new Vector3(10.7f,.055f,.045f),taxiYellow,1.4f);TaxiText(signs,"Taxi Stand",.88f,5.14f,8.36f,8.25f,.96f,.48f,detail);TaxiIcon(signs,-4.25f,4.91f,8.38f,.90f);TaxiCheckers(signs,-5.83f,4.99f,8.20f,.75f,.48f);TaxiCheckers(signs,5.83f,4.99f,8.20f,.75f,.48f);
surfaceType=0;signs.Box(new Vector3(-5.55f,1.99f,7.04f),new Vector3(1.23f,3.45f,.20f),taxiYellow,.35f);surfaceType=2;signs.Box(new Vector3(-5.55f,1.99f,7.16f),new Vector3(1.10f,3.28f,.08f),taxiBlack);TaxiIcon(signs,-5.55f,2.60f,7.23f,.60f);TaxiText(signs,"TAXI",-5.55f,1.78f,7.23f,.94f,.34f,.8f,false);TaxiCheckers(signs,-5.55f,1.05f,7.23f,1.03f,.27f);// Three bays and a rear buffer, all in the stand footprint.
surfaceType=2;ground.Box(new Vector3(0,-.025f,-.1f),new Vector3(12.65f,.09f,17.25f),taxiSteel);
surfaceType=0;foreach(float x in new[]{-5.10f,-2.55f,0f,2.55f,5.10f})TaxiYawBox(ground,new Vector3(x,.047f,1.7f),new Vector3(.105f,.018f,6.6f),12f,taxiYellow,.18f);
ground.Box(new Vector3(-1.07f,.047f,-1.42f),new Vector3(10.2f,.018f,.105f),taxiYellow,.18f);
int bay=0;foreach(float x in new[]{-3.825f,-1.275f,1.275f,3.825f}){bay++;TaxiYawBox(ground,new Vector3(x-.88f,.14f,-1.05f),new Vector3(1.45f,.24f,.26f),12f,taxiBlack);TaxiYawBox(ground,new Vector3(x-.88f,.265f,-1.05f),new Vector3(.72f,.012f,.27f),12f,taxiYellow,.1f);int first=ground.p.Count;TaxiText(ground,"0"+bay,x+.62f,0,0,.45f,.42f,.12f,false);for(int i=first;i<ground.p.Count;i++){var v=ground.p[i];ground.p[i]=new Vector3(v.x,.055f,4.8f-v.y);}}
TaxiReferenceDetails(structure,booth,signs,ground,detail);surfaceType=0;signs.Box(new Vector3(-6.12f,1.99f,7.25f),new Vector3(.035f,3.30f,.035f),taxiYellow,2f);TaxiText(signs,"STAND",-5.55f,1.35f,7.23f,.94f,.24f,.8f,false);var meshes=new List<Mesh>();foreach(var pair in new[]{new KeyValuePair<string,G>("Canopy",structure),new KeyValuePair<string,G>("Booking Booth",booth),new KeyValuePair<string,G>("Signs",signs),new KeyValuePair<string,G>("Parking Lines",ground)}){var m=new Mesh{name="Taxi "+pair.Key+(detail?" Near":" Far")};// Face the street: +Z signs are viewed with world -X to the reader's right.
for(int vertex=0;vertex<pair.Value.p.Count;vertex++){var v=pair.Value.p[vertex];v.x=-v.x;
if(pair.Key=="Booking Booth"){v.x=(v.x+3.95f)*.85f-4.85f;v.z+=6.5f;}
else if(pair.Key=="Canopy"||pair.Key=="Signs"){v.x=v.x*.75f+1.6f;}
else if(pair.Key=="Parking Lines"&&vertex>=36){v.x=v.x*.75f+1.6f;v.y+=.02f;}
if(pair.Key!="Booking Booth"&&v.x> -3.2f)v.x=-3.2f+(v.x+3.2f)*1.15f;
// Taxi-only finish identifiers stay in the existing vertex metadata and shared shader.
var tex=pair.Value.uv[vertex];var mark=pair.Value.mark[vertex];
if(pair.Key=="Canopy"){
 if(tex==taxiBlack)mark.x=v.y>4.6f?3:4;
 else if(tex==taxiSteel)mark.x=5;
 if(tex==taxiYellow&&mark.y>1)mark.x=7;
 v.y+=Mathf.Clamp01(v.y/4.4f)*.60f;
}
if(pair.Key=="Signs"){
 if(tex==taxiBlack&&(v.y>4||v.z<7.21f))mark.x=4;
 if(tex==taxiSteel)mark.x=5;
 if(tex==taxiYellow&&mark.y>1)mark.x=7;
 if(v.y>4)v.y+=.60f;
}
if(pair.Key=="Booking Booth"){
 v.x-=.80f;v.z-=1.3f;
 if(tex==taxiBlack)mark.x=3;
 if(tex==taxiSteel)mark.x=5;
 if(tex==taxiGlass)mark.x=6;
 if(tex==taxiYellow&&mark.y>1)mark.x=7;
}
if(pair.Key=="Parking Lines"&&vertex<36){
 if(v.x< -3.2f)v.x-=1.0f;
 v.z*=1.10f;
 pair.Value.uv[vertex]=new Vector2(Mathf.Lerp(.20f,.43f,Mathf.InverseLerp(-7.5f,8f,v.x)),Mathf.Lerp(.28f,.48f,Mathf.InverseLerp(-9.7f,9.7f,v.z)));
}
pair.Value.mark[vertex]=mark;
pair.Value.p[vertex]=v;}
for(int triangle=0;triangle<pair.Value.ids.Count;triangle+=3){int temp=pair.Value.ids[triangle+1];pair.Value.ids[triangle+1]=pair.Value.ids[triangle+2];pair.Value.ids[triangle+2]=temp;}
WriteG(m,pair.Value);meshes.Add(m);}UnityEngine.Object.DestroyImmediate(atlas);surfaceType=0;return meshes.ToArray();}
}









