using System;
using System.IO;
using UnityEngine;
using UnityEditor;

// Hand-selected window interiors in the existing texture atlases.
// Coordinates use a 1600px reference image (Cafe uses 1024px).
// Walls, roofing, signs and metal panels are deliberately outside every region.
public static class CityWindowMaskAuthor
{
 const int Size=1024;
 static Color32[] pixels;
 static float reference;
 static int seed; static int sharedRank=-1;
 public static Texture2D Create(Material material){
  pixels=new Color32[Size*Size];reference=material.name=="Cafe"?1024:1600;seed=17;
  switch(material.name){
   case "Building":Building();break;
   case "Building 1":BuildingOne();break;
   case "Building 2":BuildingTwo();break;
   case "Cafe":Cafe();break;
   case "Casino":Casino();break;
   // Station facade contains opaque colored panels, so leave it unlit.
  }
    var albedo=new Texture2D(2,2,TextureFormat.RGBA32,false,true);
  albedo.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap"))));
  // Trim light-colored frames and brick pixels at the edge of each selected pane.
  for(int y=0;y<Size;y++)for(int x=0;x<Size;x++){
   int index=y*Size+x;if(pixels[index].r==0)continue;
   var c=albedo.GetPixelBilinear((x+.5f)/Size,(y+.5f)/Size);
   bool brick=c.r>.15f && c.r>c.g*1.15f && c.r>c.b*1.2f;
   bool paleFrame=Mathf.Max(c.r,c.g,c.b)>.63f && c.b<c.r*1.08f;
   if(brick||paleFrame)pixels[index]=new Color32(0,0,0,255);
  }
  UnityEngine.Object.DestroyImmediate(albedo);
  string folder="Assets/Editor/CityDetailSources/Windows";Directory.CreateDirectory(folder);
  string path=folder+"/"+material.name+" Windows.png";
  var image=new Texture2D(Size,Size,TextureFormat.RGBA32,false,true);image.SetPixels32(pixels);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
  AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
  var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.sRGBTexture=false;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=Size;importer.SaveAndReimport();
  return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
 }
 static void Pane(float x,float y,float w,float h){
  seed=unchecked(seed*1664525+1013904223);byte rank=(byte)(32+(uint)seed%224);if(sharedRank>=0)rank=(byte)sharedRank;
  int left=Mathf.CeilToInt(x/reference*Size),right=Mathf.FloorToInt((x+w)/reference*Size);
  int top=Mathf.CeilToInt(y/reference*Size),bottom=Mathf.FloorToInt((y+h)/reference*Size);
  for(int yy=top;yy<bottom;yy++)for(int xx=left;xx<right;xx++)if(xx>=0&&xx<Size&&yy>=0&&yy<Size)pixels[(Size-1-yy)*Size+xx]=new Color32(rank,rank,rank,255);
 }
 static void Grid(float x,float y,float width,float height,int cols,int rows,float insetX=2,float insetY=2){
    float cw=width/cols,ch=height/rows;
  if(width<=90&&height<=65){seed=unchecked(seed*1664525+1013904223);sharedRank=(int)(32+(uint)seed%224);}else sharedRank=-1;
  for(int row=0;row<rows;row++)for(int col=0;col<cols;col++)Pane(x+col*cw+insetX,y+row*ch+insetY,cw-2*insetX,ch-2*insetY);
  sharedRank=-1;
 }
 static void Building(){
  float[] xs={26,140,196,254,368,424,481};
  float[] ys={55,138,250,333,412,496};
  foreach(var y in ys)foreach(var x in xs)Grid(x,y,26,40,2,2,2,2);
  foreach(var y in new[]{250f,333f,412f,496f})Grid(83,y,25,40,2,2,2,2);
  Grid(535,36,27,160,2,4,2,3);Grid(619,36,24,160,2,4,2,3);
  // Brick apartment windows.
  foreach(var y in new[]{40f,108f,162f,217f})foreach(var x in new[]{813f,856f,900f,943f,991f,1036f})Grid(x,y,22,30,2,2,2,2);
  foreach(var y in new[]{102f,168f,214f})foreach(var x in new[]{1218f,1278f,1337f,1394f})Grid(x,y,31,30,2,2,2,2);
  // Narrow towers and glass blocks.
  Grid(35,804,67,213,3,10,3,4);
  Grid(129,804,69,252,5,14,2,2);
  Grid(203,1068,70,26,5,2,2,2);
  Grid(905,885,114,219,4,6,3,3);
  Grid(1038,887,154,214,3,5,3,4);
  Grid(403,1321,478,149,22,7,2,2);
  Grid(1004,1205,78,270,3,12,2,3);
  // Repeated small windows of the tall pale building.
  foreach(var x in new[]{1218f,1251f,1284f,1317f,1350f,1382f})
   foreach(var y in new[]{1216f,1258f,1300f,1343f,1387f,1430f,1475f})
    Grid(x,y,18,24,2,2,1.8f,1.8f);
 }
 static void BuildingOne(){
  Grid(58,13,116,41,5,2,2,2);
  Grid(215,67,83,80,5,6,2,2);
  Grid(304,16,98,95,8,7,2,2);
  Grid(304,122,110,85,8,6,2,2);
  Grid(445,9,151,63,10,5,2,3);
  Grid(302,216,109,16,8,1,2,2);
  Grid(285,433,49,26,3,2,2,2);
  Grid(345,433,22,26,2,2,2,2);
  Grid(439,394,22,30,2,2,2,2);
  Grid(602,407,21,29,2,2,2,2);
  // The large orange, yellow and gray tiles elsewhere are metal cladding.
 }
 static void BuildingTwo(){
  foreach(var x in new[]{213f,255f,303f,341f,383f})Grid(x,121,31,32,2,1,2,2);
  Grid(216,192,57,59,3,2,3,3);Grid(343,191,37,58,2,2,3,3);
  Grid(477,43,49,33,3,2,2,2);Grid(706,43,47,33,3,2,2,2);
  Grid(469,108,80,40,6,2,2,2);Grid(581,110,24,25,2,2,2,2);Grid(643,110,23,25,2,2,2,2);
  foreach(var y in new[]{180f,217f,252f})Grid(449,y,325,14,24,1,2,2);
  foreach(var y in new[]{302f,337f,375f})Grid(449,y,230,12,18,1,2,2);
  foreach(var x in new[]{817f,847f,879f,909f,940f})Grid(x,48,9,198,1,7,1,5);
  foreach(var x in new[]{978f,1021f,1047f,1074f,1103f})Grid(x,52,13,190,1,7,2,5);
  Grid(1169,48,32,198,3,8,2,3);
  // White office building: glass interiors only, no stone bands.
  foreach(var x in new[]{817f,843f,869f,895f,921f,947f,973f,999f,1025f,1051f,1077f,1103f,1129f,1155f})Grid(x,403,13,16,1,2,1,2);
  foreach(var x in new[]{817f,851f,885f,919f,953f,987f,1021f,1055f,1089f,1123f,1157f,1191f})
   foreach(var y in new[]{455f,503f,551f,599f,651f})Grid(x,y,15,29,1,2,2,2);
  foreach(var x in new[]{1255f,1300f,1345f,1389f,1434f,1478f})
   foreach(var y in new[]{379f,455f,529f,603f})Grid(x,y,16,27,2,2,1.5f,2);
  foreach(var y in new[]{875f,900f,950f,976f,1001f,1051f})Grid(807,y,234,15,17,1,2,2);
  foreach(var y in new[]{819f,843f,869f,918f,943f,969f,995f,1020f,1045f})Grid(1173,y,166,13,12,1,2,2);
  foreach(var y in new[]{821f,845f,870f,919f,944f,969f,995f,1020f,1046f})Grid(1106,y,26,13,2,1,2,2);
  foreach(var x in new[]{822f,842f,867f,887f,911f,931f,956f,975f})
   foreach(var y in new[]{1155f,1190f,1226f,1262f,1297f,1332f,1367f,1402f,1436f,1471f,1506f,1541f,1574f})Grid(x,y,8,20,1,2,1,2);
  foreach(var x in new[]{1022f,1043f,1077f,1098f,1131f,1152f})
   foreach(var y in new[]{1155f,1190f,1231f,1265f,1300f})Grid(x,y,9,19,1,2,1,2);
  Grid(200,916,70,24,2,1,3,3);Grid(339,916,64,24,2,1,3,3);
  Grid(201,962,69,20,2,1,3,3);Grid(339,962,64,20,2,1,3,3);
 }
 static void Cafe(){
  Grid(761,656,260,96,6,2,3,3);Grid(709,796,80,95,2,2,3,3);
  Grid(389,795,107,95,2,2,4,4);Grid(670,678,43,73,1,2,3,3);
 }
 static void Casino(){
  // Select narrow glass windows, excluding the decorative tiled facade.
  Grid(18,8,632,20,48,1,2,2);
  Grid(21,707,818,13,60,1,2,2);
  Grid(1047,708,176,12,13,1,2,2);
  Grid(1378,708,208,12,15,1,2,2);
  Grid(791,1536,291,49,6,1,4,3);
 }
}

