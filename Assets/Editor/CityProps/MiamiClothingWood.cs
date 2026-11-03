using UnityEngine;
public static partial class MiamiClothingBuild {
static void BakeClothingWood(Texture2D atlas){
// Baked warm trim colour in the clothing-only palette island.
for(int y=0;y<256;y++)for(int x=0;x<256;x++)atlas.SetPixel(3584+x,2304+y,new Color(.96f,.38f,.07f));
// Bake grooves, bevel highlights and wood grain into existing clothing-only palette islands.
for(int n=0;n<2;n++)for(int y=0;y<256;y++)for(int x=0;x<256;x++){
 int tile=n==0?2:11;float t=(x%16)/16f;
 float bevel=t<.13f?.25f:t<.23f?1.27f:t>.88f?.57f:1;
 float grain=.87f+.17f*Mathf.PerlinNoise(x*.19f,y*.045f)+.06f*Mathf.PerlinNoise(x*.08f,y*.08f);
 Color c=n==0?new Color(.40f,.185f,.095f):new Color(.025f,.029f,.035f);
 atlas.SetPixel(3072+tile%4*256+x,2048+tile/4*256+y,c*bevel*grain);
}
foreach(int tile in new[]{0,1,8,9,10})for(int y=0;y<256;y++)for(int x=0;x<256;x++){
float noise=Mathf.PerlinNoise(x*.13f,y*.13f),stain=Mathf.PerlinNoise(x*.025f,y*.04f);Color c=tile==1?new Color(.016f,.019f,.024f):new Color(.31f,.33f,.35f)*(tile==8?.87f:tile==9?1.12f:1);
atlas.SetPixel(3072+tile%4*256+x,2048+tile/4*256+y,c*(.73f+.28f*noise+.12f*stain));
}}
static void WoodBand(G g, float width,float y,float z,Quaternion q,Vector3 origin,bool charcoal=false){int count=Mathf.CeilToInt(width/2);float piece=width/count;for(int i=0;i<count;i++)g.Panel(origin+q*new Vector3(-width/2+(i+.5f)*piece,y,z),piece,1.35f,Pal(charcoal?11:2),charcoal?0:6,q);}
static void WoodSurface(G g,Vector3 center,float width,float depth,bool underside){
int nx=Mathf.CeilToInt(width/2f),nz=Mathf.CeilToInt(depth/2f);float sx=width/nx,sz=depth/nz;var rotation=Quaternion.Euler(underside?90:-90,0,0);
for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)g.Panel(center+new Vector3(-width/2+(x+.5f)*sx,0,-depth/2+(z+.5f)*sz),sx,sz,Pal(2),underside?6:0,rotation);
}}




