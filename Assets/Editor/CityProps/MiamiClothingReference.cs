using UnityEngine;
public static partial class MiamiClothingBuild {
static Mesh Make(float w,float d,int lod){
var g=new G();bool near=lod==0,mid=lod<2;float z=d/2-2.8f, side=-w/2+2.8f;var turn=Quaternion.Euler(0,-90,0);
// Recessed ground-floor storefront, continuous timber canopy and raised central sign.
g.Box(new Vector3(1.4f,2.7f,-1.4f),new Vector3(w-2.8f,5.4f,d-2.8f),1);
g.Box(new Vector3(0,5.8f,0),new Vector3(w,1.35f,d),2);
g.Box(new Vector3(0,5.09f,0),new Vector3(w,.13f,d),7);
g.Box(new Vector3(0,6.5f,0),new Vector3(w,.1f,d),0);
g.Box(new Vector3(0,.36f,z+.38f),new Vector3(w-5,.72f,1.4f),0);g.Box(new Vector3(side-.38f,.36f,0),new Vector3(1.4f,.72f,d-4),0);g.Box(new Vector3(0,4.98f,z+1.35f),new Vector3(w,.12f,2.65f),2);g.Box(new Vector3(side-1.35f,4.98f,0),new Vector3(2.65f,.12f,d),2);float span=w-6, section=span/3;
for(int a=0;a<3;a++){
 float c=(1-a)*section;
 for(int j=0;j<3;j++){float x=c+(j-1)*section/3;g.Panel(new Vector3(x,2.45f,z+.025f),section/3-.07f,3.65f,Tile(a*3+j),1,Quaternion.identity);g.Box(new Vector3(x-section/6,2.47f,z+.10f),new Vector3(.055f,3.7f,.08f),7);}
 
 
 for(int st=0;st<3;st++){float zz=z+1.15f+st*.56f;float hh=.75f-st*.25f;g.Box(new Vector3(c,hh/2,zz),new Vector3(section-.3f,hh,.58f),0);g.Box(new Vector3(c,hh-.025f,zz+.30f),new Vector3(section-.34f,.032f,.022f),6,5);}
 foreach(float dx in new[]{-.82f,0,.82f})g.Box(new Vector3(c+dx,2.4f,z+.16f),new Vector3(.045f,3.6f,.09f),7);
 if(near)foreach(float dx in new[]{-.12f,.12f})g.Box(new Vector3(c+dx,1.65f,z+.25f),new Vector3(.028f,.32f,.04f),3);
}
// Stone piers with warm inset sconces.
for(int i=0;i<4;i++){float x=-span/2+i*section;for(int k=0;k<(near?6:1);k++)g.Box(new Vector3(x,(k+.5f)*5.1f/(near?6:1),z+1.85f),new Vector3(.64f,5.1f/(near?6:1)-.018f,1.9f),near?new[]{0,8,9,10}[k%4]:0);g.Box(new Vector3(x,2.75f,z+2.83f),new Vector3(.15f,1.2f,.13f),3);g.Box(new Vector3(x,2.75f,z+2.91f),new Vector3(.085f,1.03f,.025f),6,9);}
for(int n=0;n<2;n++){float x=n==0?w/2-1.5f:-w/2+1.5f;g.Box(new Vector3(x,2.7f,z+2.25f),new Vector3(2.4f,4.7f,.65f),0);g.Panel(new Vector3(x,2.8f,z+2.59f),2.02f,4.4f,Poster(n),4,Quaternion.identity);}
// Full return frontage with individual interior images and entrance steps.
float sd=d-4,sw=sd/3;
for(int a=0;a<3;a++){float c=-sd/2+(a+.5f)*sw;for(int j=0;j<3;j++){float zz=c+(j-1)*sw/3;g.Panel(new Vector3(side-.03f,2.45f,zz),sw/3-.07f,3.65f,Tile(20+a*3+j),1,turn);g.Box(new Vector3(side-.13f,2.45f,zz-sw/6),new Vector3(.08f,3.7f,.055f),7);}for(int st=0;st<3;st++){float xx=side-1.15f-st*.56f,hh=.75f-st*.25f;g.Box(new Vector3(xx,hh/2,c),new Vector3(.58f,hh,sw-.3f),0);g.Box(new Vector3(xx-.30f,hh-.025f,c),new Vector3(.022f,.032f,sw-.34f),6,5);}}
foreach(float zz in new[]{-sd/2,-sd/2+sw,-sd/2+2*sw,sd/2}){g.Box(new Vector3(side-1.85f,2.55f,zz),new Vector3(1.9f,5.1f,.64f),0);g.Box(new Vector3(side-2.83f,2.75f,zz),new Vector3(.13f,1.2f,.15f),3);g.Box(new Vector3(side-2.91f,2.75f,zz),new Vector3(.025f,1.03f,.085f),6,9);}
// Timber battens on fascia and soffit; reduced spacing detail at distance.
WoodBand(g,w,5.8f,d/2+.035f,Quaternion.identity,Vector3.zero);WoodBand(g,d,5.8f,w/2+.035f,turn,Vector3.zero);WoodBand(g,w,5.8f,d/2+.035f,Quaternion.Euler(0,180,0),Vector3.zero);WoodBand(g,d,5.8f,w/2+.035f,Quaternion.Euler(0,90,0),Vector3.zero);
g.Box(new Vector3(0,6.45f,d/2+.06f),new Vector3(w,.055f,.065f),6,5);g.Box(new Vector3(-w/2-.06f,6.45f,0),new Vector3(.065f,.055f,d),6,5);
float signz=d/2+.18f;g.Box(new Vector3(0,7.3f,signz),new Vector3(16,3.3f,.42f),11);
// Layered charcoal frame and deep textured side returns around the sign.
g.Box(new Vector3(0,7.3f,signz-.18f),new Vector3(16.24f,3.52f,.25f),7);
foreach(float xx in new[]{-8.06f,8.06f})g.Box(new Vector3(xx,7.3f,signz+.12f),new Vector3(.11f,3.42f,.48f),7);
foreach(float yy in new[]{5.61f,8.99f})g.Box(new Vector3(0,yy,signz+.12f),new Vector3(16.2f,.10f,.48f),7);
g.Panel(new Vector3(-8.125f,7.3f,signz-.04f),.48f,3.35f,Pal(11),7,Quaternion.Euler(0,-90,0));
g.Panel(new Vector3(8.125f,7.3f,signz-.04f),.48f,3.35f,Pal(11),7,Quaternion.Euler(0,90,0));
for(int strip=0;strip<16;strip++)g.Panel(new Vector3(-7.5f+strip,7.3f,signz+.218f),1,3.25f,Pal(11),7,Quaternion.identity);
if(mid){Letter(g,"CLOTHING",new Vector3(3.0f,6.9f,signz+.23f),1.0f,4,2,near,Quaternion.identity);Letter(g,"STORE",new Vector3(-4.1f,6.9f,signz+.23f),1.0f,3,3,near,Quaternion.identity);}else g.Panel(new Vector3(0,6.85f,signz+.23f),12,1.1f,new Rect(3076/4096f,2948/3072f,1016/4096f,120/3072f),2,Quaternion.identity);
Crown(g,new Vector3(0,8.12f,signz+.23f),.85f,near,Quaternion.identity);foreach(float yy in new[]{5.68f,8.94f})g.Box(new Vector3(0,yy,signz+.25f),new Vector3(16,.055f,.06f),6,5);
// Terrace railing and open timber pergola, without plants or furniture.
if(mid){foreach(float zz in new[]{-d/2,d/2-.1f}){g.Box(new Vector3(0,7.6f,zz),new Vector3(w,.05f,.05f),7);for(float x=-w/2;x<=w/2;x+=2.8f)g.Box(new Vector3(x,7.05f,zz),new Vector3(.045f,1.1f,.045f),7);}foreach(float xx in new[]{-w/2,w/2})g.Box(new Vector3(xx,7.6f,0),new Vector3(.05f,.05f,d),7);float px=-w*.26f;foreach(float x in new[]{px-3.8f,px+3.8f})foreach(float zz in new[]{-3f,3f})g.Box(new Vector3(x,7.95f,zz),new Vector3(.17f,2.9f,.17f),2);for(float x=px-4;x<=px+4;x+=near?.38f:.76f)g.Box(new Vector3(x,9.4f,0),new Vector3(.14f,.16f,6.6f),2);}
WoodSurface(g,new Vector3(0,4.905f,z+1.35f),w,2.65f,true);WoodSurface(g,new Vector3(side-1.35f,4.905f,-1.325f),2.65f,d-2.65f,true);WoodSurface(g,new Vector3(0,6.565f,0),w,d,false);// Opaque baked-style wall wash and narrow illuminated mortar joints, within the shared mesh.
for(int i=0;i<4;i++){float xx=-span/2+i*section;g.Panel(new Vector3(xx,2.55f,z+2.808f),.64f,5.1f,Pal(0),8,Quaternion.identity);for(int k=1;k<6;k++)g.Box(new Vector3(xx,k*5.1f/6,z+2.824f),new Vector3(.60f,.012f,.018f),6,5);}
foreach(float zz in new[]{-sd/2,-sd/2+sw,-sd/2+2*sw,sd/2}){g.Panel(new Vector3(side-2.808f,2.55f,zz),.64f,5.1f,Pal(0),8,turn);for(int k=1;k<6;k++)g.Box(new Vector3(side-2.824f,k*5.1f/6,zz),new Vector3(.018f,.012f,.60f),6,5);}
// Lift the canopy, fascia, main sign and terrace to clear the doubled department headers.
for(int v=0;v<g.p.Count;v++){var p=g.p[v];if(p.y>=4.9f){p.y+=.6f;g.p[v]=p;}}
for(int a=0;a<3;a++){float c=(1-a)*section;g.Box(new Vector3(c,4.87f,z+.12f),new Vector3(section-.15f,1.2f,.25f),1);if(mid)Letter(g,new[]{"MEN","WOMEN","ACCESSORIES"}[a],new Vector3(c,4.87f,z+.27f),.38f,4,2,near,Quaternion.identity);float zz=-sd/2+(a+.5f)*sw;g.Box(new Vector3(side-.14f,4.87f,zz),new Vector3(.25f,1.2f,sw-.1f),1);if(mid)Letter(g,new[]{"MEN","WOMEN","ACCESSORIES"}[a],new Vector3(side-.29f,4.87f,zz),.35f,4,2,near,turn);}
return g.Save("Miami Clothing Store LOD"+lod);
}}






