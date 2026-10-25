using System.Diagnostics;
using System.Text.Json;
using XdcConverter;
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var linear=Enumerable.Range(0,256).Select(v=>ColorMixer.Linear(v/255.0)).ToArray();
ColorMixer.Rgb C(int c)=>new(linear[(c>>16)&255],linear[(c>>8)&255],linear[c&255]);
ColorMixer.Rgb[] Patch(byte[]? rgb,int[]? screen,int size=8) {
 var result=new ColorMixer.Rgb[(160/size)*(200/size)];int j=0;
 for(int y=0;y<200;y+=size)for(int x=0;x<160;x+=size){var sum=new ColorMixer.Rgb();
  for(int yy=y;yy<y+size;yy++)for(int xx=x;xx<x+size;xx++)if(rgb!=null){int i=(yy*160+xx)*3;sum+=C((rgb[i]<<16)|(rgb[i+1]<<8)|rgb[i+2]);}else for(int d=0;d<4;d++)sum+=C(screen![yy*640+xx*4+d]);
  result[j++]=ColorMixer.Lab(sum*(rgb!=null?1.0/(size*size):1.0/(size*size*4)));
 }return result;
}
double Error(ColorMixer.Rgb[] a,ColorMixer.Rgb[] b)=>a.Zip(b,ColorMixer.Distance).Average();
double Temporal(ColorMixer.Rgb[] a,ColorMixer.Rgb[] previous,ColorMixer.Rgb[] source,ColorMixer.Rgb[] prevSource){double sum=0;for(int i=0;i<a.Length;i++)sum+=ColorMixer.Distance(a[i]-previous[i],source[i]-prevSource[i]);return sum/a.Length;}
var clips=new Dictionary<int,List<byte[]>>();
foreach(int time in new[]{30,80,150}){
 var psi=new ProcessStartInfo(Path.Combine(root,".tools/ffmpeg/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 foreach(var a in new[]{"-v","error","-ss",(time-1).ToString(),"-i",Path.Combine(root,"local/media/MIR.webm"),"-vf","scale=160:200:flags=lanczos,setsar=1,fps=30","-frames:v","120","-pix_fmt","rgb24","-f","rawvideo","pipe:1"})psi.ArgumentList.Add(a);
 using var proc=Process.Start(psi)!;var frames=new List<byte[]>();for(int f=0;f<120;f++){var rgb=new byte[96000];proc.StandardOutput.BaseStream.ReadExactly(rgb);frames.Add(rgb);}proc.WaitForExit();if(proc.ExitCode!=0)throw new Exception(proc.StandardError.ReadToEnd());clips.Add(time,frames);
}
var reports=new List<object>();
foreach(string mask in new[]{"bayer","bayer4","blue"})foreach(int stability in new[]{0,50}){
 var job=new Job{Dither=mask,TemporalStability=stability};var writer=new FrameWriter(job);var rows=new List<object>();
 foreach(var clip in clips){var encoder=new NativeEncoder(1,200,30,150,735,75);var stabilizer=stability>0?new NtscStabilizer(job):null;
  ColorMixer.Rgb[]? prevTarget=null,prevActual=null;double color=0,detail=0,temporal=0;int count=0;long videoBytes=0;var timer=Stopwatch.StartNew();
  for(int f=0;f<clip.Value.Count;f++){var rgb=clip.Value[f];var pixels=writer.Quantize(rgb);stabilizer?.Apply(rgb,pixels,encoder.Reconstructed);
   var packet=stabilizer==null?encoder.Encode(pixels):encoder.Encode(pixels,stabilizer.Benefits,stabilizer.SceneCut);if(f<30)continue;videoBytes+=packet.Length;
   var screen=CgaNtsc.Render(encoder.Reconstructed,new());var target=Patch(rgb,null);var actual=Patch(null,screen);color+=Error(target,actual);detail+=Error(Patch(rgb,null,2),Patch(null,screen,2));
   if(prevTarget!=null)temporal+=Temporal(actual,prevActual!,target,prevTarget);prevTarget=target;prevActual=actual;count++;
  }
  var r=new{Mask=mask,Stability=stability,Start=clip.Key,ColorError=color/count,DetailError=detail/count,TemporalError=temporal/(count-1),VideoBytes=videoBytes,Seconds=timer.Elapsed.TotalSeconds,Encoder=encoder.Statistics,Stabilization=stabilizer?.Statistics};rows.Add(r);Console.WriteLine(JsonSerializer.Serialize(r));
 }
 reports.Add(new{Mask=mask,Stability=stability,Clips=rows});
}
File.WriteAllText(Path.Combine(output,"pilot.json"),JsonSerializer.Serialize(reports,Data.Json));
