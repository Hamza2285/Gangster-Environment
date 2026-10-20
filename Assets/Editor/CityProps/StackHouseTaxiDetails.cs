using System.Collections.Generic;using UnityEngine;
public static partial class StackHouse3DSigns{
static void TaxiYawBox(G g,Vector3 p,Vector3 size,float angle,Vector2 color,float glow=0){int first=g.p.Count;g.Box(p,size,color,glow);var rotation=Quaternion.Euler(0,angle,0);for(int i=first;i<g.p.Count;i++)g.p[i]=p+rotation*(g.p[i]-p);}
static void TaxiIsland(G g,Vector3 p,Vector2 size,bool detail){surfaceType=2;g.Box(p+new Vector3(0,.13f,0),new Vector3(size.x,.26f,size.y),taxiSteel);surfaceType=0;float step=detail?.48f:1.5f;int n=Mathf.CeilToInt(size.y/step);for(int i=0;i<n;i++){var color=i%2==0?taxiYellow:taxiBlack;float z=p.z-size.y*.5f+(i+.5f)*size.y/n;foreach(float x in new[]{p.x-size.x*.5f,p.x+size.x*.5f})g.Box(new Vector3(x,.12f,z),new Vector3(.065f,.24f,size.y/n-.008f),color,.05f);}int across=Mathf.Max(2,Mathf.CeilToInt(size.x/.50f));for(int i=0;i<across;i++){float x=p.x-size.x*.5f+(i+.5f)*size.x/across;foreach(float z in new[]{p.z-size.y*.5f,p.z+size.y*.5f})g.Box(new Vector3(x,.12f,z),new Vector3(size.x/across-.008f,.24f,.065f),i%2==0?taxiYellow:taxiBlack,.05f);}}
static void TaxiReferenceDetails(G structure,G booth,G signs,G ground,bool detail){
// Separate raised walkways on the edges keep all three diagonal bays accessible.
TaxiIsland(ground,new Vector3(5.88f,0,1.75f),new Vector2(.76f,12.1f),detail);
TaxiIsland(ground,new Vector3(-5.88f,0,.675f),new Vector2(.76f,9.95f),detail);
// Dedicated front island: the tall taxi pylon rises from its pavement, clear of the bay mouth.
TaxiIsland(ground,new Vector3(-5.55f,0,6.95f),new Vector2(1.28f,2.55f),detail);
// Booth plinth and accessible rear walkway are kept outside the parking bays.
TaxiIsland(booth,new Vector3(3.95f,0,-6.5f),new Vector2(4.0f,4.0f),detail);
// Window reveals, lower panel seams and a ticket/pass-through ledge.
Vector3 bc=new Vector3(3.95f,0,-6.5f);surfaceType=2;
foreach(float x in new[]{-1.63f,1.63f})booth.Box(bc+new Vector3(x,1.95f,1.51f),new Vector3(.085f,1.55f,.14f),taxiSteel);
booth.Box(bc+new Vector3(0,2.64f,1.51f),new Vector3(3.30f,.08f,.14f),taxiSteel);
booth.Box(bc+new Vector3(.77f,1.92f,1.53f),new Vector3(.055f,1.37f,.10f),taxiSteel);
booth.Box(bc+new Vector3(-.35f,1.48f,1.65f),new Vector3(1.42f,.07f,.40f),taxiSteel);
booth.Box(bc+new Vector3(-.35f,1.40f,1.58f),new Vector3(.66f,.035f,.065f),taxiBlack);
if(detail){foreach(float x in new[]{-1.06f,0,1.06f})booth.Box(bc+new Vector3(x,.68f,1.51f),new Vector3(.024f,.68f,.025f),taxiSteel);foreach(float z in new[]{-.9f,0,.9f})booth.Box(bc+new Vector3(1.68f,.68f,z),new Vector3(.025f,.68f,.024f),taxiSteel);}
surfaceType=0;booth.Box(bc+new Vector3(0,3.11f,1.82f),new Vector3(3.55f,.04f,.035f),taxiYellow,1.1f);
// Recessed canopy bays and visible warm ceiling panels, rather than new realtime lights.
if(detail){surfaceType=2;for(int k=0;k<7;k++)structure.Box(new Vector3(0,4.40f,-3.75f+k*1.72f),new Vector3(11.8f,.025f,.022f),taxiSteel);foreach(float x in new[]{-5.65f,5.65f})foreach(float z in new[]{-3.7f,6.95f}){structure.Box(new Vector3(x,4.15f,z),new Vector3(.42f,.38f,.42f),taxiSteel);}}
// Close-range construction details are omitted entirely from the far LOD.
if(detail){
 surfaceType=2;
 // Folded metal perimeter channels, roof panel seams and drainage gutter.
 foreach(float x in new[]{-6.30f,6.30f}){
  structure.Box(new Vector3(x,4.66f,1.8f),new Vector3(.085f,.065f,12.6f),taxiSteel);
  structure.Box(new Vector3(x,5.20f,1.8f),new Vector3(.085f,.045f,12.8f),taxiSteel);
 }
 structure.Box(new Vector3(0,5.23f,-4.55f),new Vector3(12.65f,.10f,.17f),taxiBlack);
 for(int panel=0;panel<6;panel++)structure.Box(new Vector3(-5.1f+panel*2.04f,5.21f,1.8f),new Vector3(.025f,.025f,12.4f),taxiSteel);
 // Steel base plates, anchor heads and paired column collars.
 foreach(float x in new[]{-5.65f,5.65f})foreach(float z in new[]{-3.7f,6.95f}){
  structure.Box(new Vector3(x,.025f,z),new Vector3(.69f,.055f,.69f),taxiSteel);
  foreach(float dx in new[]{-.245f,.245f})foreach(float dz in new[]{-.245f,.245f})structure.Box(new Vector3(x+dx,.069f,z+dz),new Vector3(.065f,.035f,.065f),taxiWhite);
  foreach(float y in new[]{.42f,3.65f})structure.Box(new Vector3(x,y,z),new Vector3(.33f,.065f,.33f),taxiSteel);
 }
 // Ceiling lamp bezels and separate inset luminous faces already use the shared atlas.
 foreach(float x in new[]{-4f,0,4f})foreach(float z in new[]{-1.5f,3.7f}){
  structure.Box(new Vector3(x,4.295f,z),new Vector3(.56f,.025f,.32f),taxiSteel);
  surfaceType=0;structure.Box(new Vector3(x,4.277f,z),new Vector3(.45f,.018f,.22f),taxiWhite,2.2f);surfaceType=2;
 }
 // Dimensional main-sign surround with inset corner fasteners.
 signs.Box(new Vector3(0,5.13f,8.235f),new Vector3(10.91f,1.71f,.045f),taxiSteel);
 foreach(float x in new[]{-5.26f,5.26f})foreach(float y in new[]{4.48f,5.78f})signs.Box(new Vector3(x,y,8.315f),new Vector3(.047f,.047f,.025f),taxiWhite);
 // Booking booth: framed side access door, handle, window seals and a real vent grille.
 surfaceType=2;
 booth.Box(bc+new Vector3(1.705f,1.46f,-.32f),new Vector3(.055f,2.48f,1.13f),taxiSteel);
 booth.Box(bc+new Vector3(1.745f,.86f,-.32f),new Vector3(.035f,1.18f,.99f),taxiBlack);
 surfaceType=1;booth.Box(bc+new Vector3(1.752f,2.0f,-.32f),new Vector3(.018f,.91f,.93f),taxiGlass);surfaceType=2;
 booth.Box(bc+new Vector3(1.79f,1.24f,.10f),new Vector3(.09f,.24f,.045f),taxiWhite);
 booth.Box(bc+new Vector3(1.72f,.66f,1.04f),new Vector3(.06f,.45f,.57f),taxiSteel);
 for(int slot=0;slot<5;slot++)booth.Box(bc+new Vector3(1.76f,.50f+slot*.078f,1.04f),new Vector3(.025f,.025f,.46f),taxiBlack);
 // Lower window drip rail, deep service-window reveal and ticket tray.
 booth.Box(bc+new Vector3(0,1.39f,1.61f),new Vector3(3.26f,.055f,.19f),taxiSteel);
 booth.Box(bc+new Vector3(-.35f,1.64f,1.58f),new Vector3(1.51f,.055f,.12f),taxiSteel);
 foreach(float x in new[]{-.35f-.73f,-.35f+.73f})booth.Box(bc+new Vector3(x,1.54f,1.59f),new Vector3(.045f,.23f,.11f),taxiSteel);
 booth.Box(bc+new Vector3(-.35f,1.52f,1.72f),new Vector3(.37f,.018f,.18f),taxiBlack);
 // Raised paving slab joints along the walkway tops, plus nonslip grooves at the booth threshold.
 surfaceType=0;
 for(int i=0;i<19;i++)ground.Box(new Vector3(5.88f,.263f,-4.0f+i*.62f),new Vector3(.63f,.006f,.012f),taxiBlack);
 for(int i=0;i<15;i++)ground.Box(new Vector3(-5.88f,.263f,-4.0f+i*.62f),new Vector3(.63f,.006f,.012f),taxiBlack);
 for(int i=0;i<5;i++)booth.Box(bc+new Vector3(1.93f,.263f,-.72f+i*.18f),new Vector3(.12f,.006f,.025f),taxiBlack);
 // Roof drainage fittings, a ticket drop surround and corner joint plates.
 surfaceType=2;
 foreach(float x in new[]{-5.65f,5.65f}){
  structure.Box(new Vector3(x,2.4f,-3.85f),new Vector3(.075f,4.2f,.075f),taxiSteel);
  structure.Box(new Vector3(x,4.47f,-3.93f),new Vector3(.17f,.11f,.25f),taxiSteel);
 }
 booth.Box(bc+new Vector3(-.35f,1.17f,1.56f),new Vector3(.44f,.17f,.045f),taxiSteel);
 booth.Box(bc+new Vector3(-.35f,1.19f,1.59f),new Vector3(.32f,.035f,.018f),taxiBlack);
 foreach(float x in new[]{-1.64f,1.64f})booth.Box(bc+new Vector3(x,.32f,1.54f),new Vector3(.16f,.24f,.045f),taxiSteel);}}
}



