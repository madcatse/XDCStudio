using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using XdcConverter;
try {
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]),folder=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
string ffmpeg=Path.Combine(root,".tools/ffmpeg/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe");
var linear=Enumerable.Range(0,256).Select(v=>ColorMixer.Linear(v/255.0)).ToArray();
ColorMixer.Rgb Color(int c)=>new(linear[(c>>16)&255],linear[(c>>8)&255],linear[c&255]);
ColorMixer.Rgb[] Patch(byte[]? rgb,int[]? screen,int size=8){var result=new ColorMixer.Rgb[(160/size)*(200/size)];int k=0;
 for(int y=0;y<200;y+=size)for(int x=0;x<160;x+=size){var mean=new ColorMixer.Rgb();for(int yy=y;yy<y+size;yy++)for(int xx=x;xx<x+size;xx++)if(rgb!=null){int p=(yy*160+xx)*3;mean+=Color((rgb[p]<<16)|(rgb[p+1]<<8)|rgb[p+2]);}else for(int d=0;d<4;d++)mean+=Color(screen![yy*640+xx*4+d]);result[k++]=ColorMixer.Lab(mean*(rgb!=null?1.0/(size*size):1.0/(size*size*4)));}return result;
}
double Error(ColorMixer.Rgb[] a,ColorMixer.Rgb[] b)=>a.Zip(b,ColorMixer.Distance).Average();
double Temporal(ColorMixer.Rgb[] a,ColorMixer.Rgb[] old,ColorMixer.Rgb[] target,ColorMixer.Rgb[] oldTarget){double sum=0;for(int i=0;i<a.Length;i++)sum+=ColorMixer.Distance(a[i]-old[i],target[i]-oldTarget[i]);return sum/a.Length;}
Process Start(string[] arguments,bool input=false){var psi=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardInput=input,RedirectStandardOutput=!input};foreach(var a in arguments)psi.ArgumentList.Add(a);return Process.Start(psi)!;}
string[] names=["LEGACY05","B8_OFF","MIR_CGA","B4_OFF","B4_STAB","BLUE_OFF","BLUE_STB"];
var readers=names.Select(n=>new XdvReader(n=="LEGACY05"?Path.Combine(root,"local/reference/0.5/MIR_CGA.XDV"):Path.Combine(folder,n+".XDV"))).ToArray();
var sums=names.Select(_=>new double[2]).ToArray();var temporal=names.Select(_=>new Dictionary<int,double[]>()).ToArray();var previous=names.Select(_=>new Dictionary<int,ColorMixer.Rgb[]>()).ToArray();var prevSources=new Dictionary<int,ColorMixer.Rgb[]>();int samples=0,videoFrames=0;
using var decoder=Start(["-v","error","-ss","0","-i",Path.Combine(root,"local/media/MIR.webm"),"-vf","scale=160:200:flags=lanczos,setsar=1,fps=30,tpad=stop_mode=clone:stop_duration=1","-frames:v","6042","-pix_fmt","rgb24","-f","rawvideo","pipe:1"]);var decErrors=decoder.StandardError.ReadToEndAsync();
using var movie=Start(["-v","error","-y","-f","rawvideo","-pixel_format","bgra","-video_size","960x520","-framerate","30","-i","pipe:0","-an","-c:v","libx264","-crf","18","-pix_fmt","yuv420p","-movflags","+faststart",Path.Combine(output,"comparison.mp4")],true);var movErrors=movie.StandardError.ReadToEndAsync();
var rgb=new byte[96000];var pixels=new byte[960*520*4];
for(int frame=0;frame<6042;frame++){
 decoder.StandardOutput.BaseStream.ReadExactly(rgb);int segment=new[]{30,80,150}.FirstOrDefault(t=>frame>=t*30&&frame<(t+3)*30);
 if(frame%30!=0&&segment==0)continue;var target=Patch(rgb,null);var detail=frame%30==0?Patch(rgb,null,2):null;
 var ram=new byte[names.Length][];
 for(int n=0;n<names.Length;n++){
  ram[n]=readers[n].ReadFrame(frame);var screen=CgaNtsc.Render(ram[n],new());var actual=Patch(null,screen);
  if(frame%30==0){sums[n][0]+=Error(target,actual);sums[n][1]+=Error(detail!,Patch(null,screen,2));}
  if(segment!=0){if(!temporal[n].TryGetValue(segment,out var a))temporal[n][segment]=a=new double[3];a[0]+=Error(target,actual);a[2]++;
   if(previous[n].TryGetValue(segment,out var old))a[1]+=Temporal(actual,old,target,prevSources[segment]);previous[n][segment]=actual;
  }
 }
 if(frame%30==0)samples++;
 if(segment!=0){
  prevSources[segment]=target;using var board=new Bitmap(960,520,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(board)){
   g.Clear(System.Drawing.Color.FromArgb(242,245,248));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;using var font=new Font("Segoe UI",10);using var source=FrameImage.FromRgb(rgb,160,200);
   string[] captions=["Исходник · 160×200","0.6 · Bayer 8×8","0.7 · Bayer 8×8 + стабилизация","0.7 · Bayer 4×4 + стабилизация","0.7 · Blue noise + стабилизация"];
   int[] indices=[-1,1,2,4,6];for(int tile=0;tile<5;tile++){
    int x=tile%3*320,y=tile/3*260;g.DrawString(captions[tile],font,Brushes.Black,x+5,y+2);
    if(tile==0)g.DrawImage(source,new Rectangle(x,y+20,320,240));else{using var image=FrameImage.Create(ram[indices[tile]],1,new());g.DrawImage(image,new Rectangle(x,y+20,320,240));}
   }
   g.DrawString($"MIR.webm · {frame/30.0:F2} с\n\nГотовые XDV через одну NTSC-модель\nРанняя CGA · 30 FPS · 150 КиБ/с\nСтабилизация: 50%\n\nФрагменты: 30–33, 80–83, 150–153 с",font,Brushes.Black,650,285);
  }
  if(frame==segment*30)board.Save(Path.Combine(output,$"compare-{segment}.png"));
  var data=board.LockBits(new Rectangle(0,0,960,520),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{Marshal.Copy(data.Scan0,pixels,0,pixels.Length);}finally{board.UnlockBits(data);}movie.StandardInput.BaseStream.Write(pixels);videoFrames++;
 }
}
decoder.WaitForExit();movie.StandardInput.Close();movie.WaitForExit();if(decoder.ExitCode!=0||movie.ExitCode!=0)throw new Exception(decErrors.Result+movErrors.Result);
var rows=new List<object>();for(int n=0;n<names.Length;n++){
 rows.Add(new{Name=names[n],ColorError=sums[n][0]/samples,DetailError=sums[n][1]/samples,FileBytes=readers[n].Info.Bytes,Motion=temporal[n].Select(k=>new{Start=k.Key,Frames=(int)k.Value[2],ColorError=k.Value[0]/k.Value[2],TemporalError=k.Value[1]/(k.Value[2]-1)}).ToArray()});readers[n].Dispose();
}
var report=new{Success=true,Frames=6042,SampledFrames=samples,MotionFrames=videoFrames,Model="early CGA, hue 0, chroma 1",Results=rows};File.WriteAllText(Path.Combine(output,"comparison.json"),JsonSerializer.Serialize(report,Data.Json));Console.WriteLine(JsonSerializer.Serialize(report,Data.Json));
} catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
