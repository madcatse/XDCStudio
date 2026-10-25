using System.Diagnostics;
using System.Text.Json;
using XdcConverter;
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var linear=Enumerable.Range(0,256).Select(v=>ColorMixer.Linear(v/255.0)).ToArray();
ColorMixer.Rgb Color(int c)=>new(linear[(c>>16)&255],linear[(c>>8)&255],linear[c&255]);
ColorMixer.Rgb[] Patches(byte[]? rgb,int[]? screen) {
 var result=new ColorMixer.Rgb[500];int j=0;
 for(int y=0;y<200;y+=8)for(int x=0;x<160;x+=8){var sum=new ColorMixer.Rgb();
  for(int yy=y;yy<y+8;yy++)for(int xx=x;xx<x+8;xx++){
   if(rgb!=null){int i=(yy*160+xx)*3;sum+=Color((rgb[i]<<16)|(rgb[i+1]<<8)|rgb[i+2]);}
   else for(int d=0;d<4;d++)sum+=Color(screen![yy*640+xx*4+d]);
  }result[j++]=ColorMixer.Lab(sum*(rgb!=null?1/64.0:1/256.0));
 }return result;
}
double Error(ColorMixer.Rgb[] a,ColorMixer.Rgb[] b)=>a.Zip(b,ColorMixer.Distance).Average();
double Temporal(ColorMixer.Rgb[] a,ColorMixer.Rgb[] previous,ColorMixer.Rgb[] source,ColorMixer.Rgb[] prevSource){double sum=0;for(int i=0;i<a.Length;i++)sum+=ColorMixer.Distance(a[i]-previous[i],source[i]-prevSource[i]);return sum/a.Length;}
var psi=new ProcessStartInfo(Path.Combine(root,".tools/ffmpeg/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
foreach(var a in new[]{"-v","error","-ss","0","-i",Path.Combine(root,"local/media/MIR.webm"),"-vf","scale=160:200:flags=lanczos,setsar=1,fps=30","-frames:v","4590","-pix_fmt","rgb24","-f","rawvideo","pipe:1"})psi.ArgumentList.Add(a);
using var process=Process.Start(psi)!;var frames=new Dictionary<int,List<byte[]>>{{30,[]},{80,[]},{150,[]}};var buffer=new byte[96000];
for(int f=0;f<4590;f++){process.StandardOutput.BaseStream.ReadExactly(buffer);foreach(int t in frames.Keys)if(f>=t*30&&f<(t+3)*30)frames[t].Add((byte[])buffer.Clone());}
process.WaitForExit();if(process.ExitCode!=0)throw new Exception(process.StandardError.ReadToEnd());
var report=new List<object>();
foreach(bool newer in new[]{false,true}){
 var writer=new FrameWriter(new Job{CompositeAlgorithm=newer?"ntsc":"legacy"});
 using var reader=new XdvReader(Path.Combine(root,newer?"local/reference/0.6/MIR_CGA.XDV":"local/reference/0.5/MIR_CGA.XDV"));
 foreach(var segment in frames){double preError=0,postError=0,preTemporal=0,postTemporal=0;long changedPixels=0,changedBytes=0,nearStable=0,nearStableChanged=0;int f=0;
  ColorMixer.Rgb[]? prevTarget=null,prevPre=null,prevPost=null;byte[]? prevRgb=null,prevPixels=null,prevRam=null;
  foreach(var rgb in segment.Value){var pixels=writer.Quantize(rgb);var ram=new byte[16384];NativeEncoder.PackPixels(pixels,1,200,ram);
   var target=Patches(rgb,null);var pre=Patches(null,CgaNtsc.Render(ram,new()));var post=Patches(null,CgaNtsc.Render(reader.ReadFrame(segment.Key*30+f),new()));
   preError+=Error(target,pre);postError+=Error(target,post);
   if(prevTarget!=null){preTemporal+=Temporal(pre,prevPre!,target,prevTarget);postTemporal+=Temporal(post,prevPost!,target,prevTarget);
    for(int i=0;i<pixels.Length;i++){bool changed=pixels[i]!=prevPixels![i];if(changed)changedPixels++;
     int p=i*3;bool near=Math.Abs(rgb[p]-prevRgb![p])<=2&&Math.Abs(rgb[p+1]-prevRgb[p+1])<=2&&Math.Abs(rgb[p+2]-prevRgb[p+2])<=2;
     if(near){nearStable++;if(changed)nearStableChanged++;}
    }
    for(int i=0;i<ram.Length;i++)if(ram[i]!=prevRam![i])changedBytes++;
   }
   prevTarget=target;prevPre=pre;prevPost=post;prevRgb=rgb;prevPixels=pixels;prevRam=ram;f++;
  }
  var result=new{Algorithm=newer?"NTSC06":"Legacy05",Start=segment.Key,Frames=f,PreparedColorError=preError/f,EncodedColorError=postError/f,PreparedTemporalError=preTemporal/(f-1),EncodedTemporalError=postTemporal/(f-1),MeanChangedPixels=changedPixels/(double)(f-1),MeanChangedBytes=changedBytes/(double)(f-1),NearlyUnchangedPixels=nearStable,ChangesAmongNearlyUnchanged=nearStableChanged,NearlyUnchangedFlipPercent=100.0*nearStableChanged/nearStable};report.Add(result);Console.WriteLine(JsonSerializer.Serialize(result));
 }
}
File.WriteAllText(Path.Combine(output,"diagnosis.json"),JsonSerializer.Serialize(report,Data.Json));
