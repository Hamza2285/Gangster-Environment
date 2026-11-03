using System.Linq;using UnityEngine;
public static partial class MiamiClothingBuild{static void GunLeftLine(G g,string text,float edge,float y,float z,float height){int start=g.p.Count;GunLetter(g,text,new Vector3(0,y,z),height,false,false);float delta=edge-g.p.Skip(start).Max(p=>p.x);for(int i=start;i<g.p.Count;i++){var p=g.p[i];p.x+=delta;g.p[i]=p;}}}
