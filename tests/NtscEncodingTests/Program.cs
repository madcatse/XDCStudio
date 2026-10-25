using System.Diagnostics;
using System.Text.Json;
using XdcConverter;

string root=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
int checks=0;
void Check(bool condition,string message) { if(!condition) throw new Exception(message); checks++; }
string ffmpeg=Path.Combine(root,".tools/ffmpeg/ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe");
Process Decode(double time, int frames) {
 var psi=new ProcessStartInfo(ffmpeg){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
 foreach(var a in new[]{"-v","error","-ss",Data.N(time),"-i",Path.Combine(root,"local/media/MIR.webm"),"-vf","scale=160:200:flags=lanczos,setsar=1,fps=30,tpad=stop_mode=clone:stop_duration=1","-frames:v",frames.ToString(),"-pix_fmt","rgb24","-f","rawvideo","pipe:1"}) psi.ArgumentList.Add(a);
 return Process.Start(psi)!;
}
ColorMixer.Rgb[] Reference(byte[] rgb,int size) {
 var result=new ColorMixer.Rgb[(160/size)*(200/size)]; int j=0;
 for(int y=0;y+size<=200;y+=size) for(int x=0;x+size<=160;x+=size) {
  var mean=new ColorMixer.Rgb();
  for(int yy=y;yy<y+size;yy++) for(int xx=x;xx<x+size;xx++) {int i=(yy*160+xx)*3;mean+=ColorMixer.FromColor((rgb[i]<<16)|(rgb[i+1]<<8)|rgb[i+2]);}
  result[j++]=ColorMixer.Lab(mean*(1.0/(size*size)));
 } return result;
}
ColorMixer.Rgb[] Actual(int[] pixels,int size) {
 var result=new ColorMixer.Rgb[(160/size)*(200/size)]; int j=0;
 for(int y=0;y+size<=200;y+=size) for(int x=0;x+size<=160;x+=size) {
  var mean=new ColorMixer.Rgb();
  for(int yy=y;yy<y+size;yy++) for(int xx=x*4;xx<(x+size)*4;xx++) mean+=ColorMixer.FromColor(pixels[yy*640+xx]&0xffffff);
  result[j++]=ColorMixer.Lab(mean*(1.0/(size*size*4)));
 } return result;
}
double Error(ColorMixer.Rgb[] expected,ColorMixer.Rgb[] actual)=>expected.Zip(actual,(a,b)=>ColorMixer.Distance(a,b)).Average();
byte[] Pack(byte[] pixels) {var ram=new byte[16384];NativeEncoder.PackPixels(pixels,1,200,ram);return ram;}
var watch=Stopwatch.StartNew(); var newer=new FrameWriter(new Job()); double cold=watch.Elapsed.TotalSeconds;
var older=new FrameWriter(new Job{CompositeAlgorithm="legacy"});
Console.WriteLine($"NTSC cold table: {cold:F3}s");
var stills=new List<object>();double oldMean=0,newMean=0,oldFine=0,newFine=0;
foreach(int time in new[]{30,50,80,110,150,180}) {
 using var process=Decode(time,1);var rgb=new byte[96000];process.StandardOutput.BaseStream.ReadExactly(rgb);process.WaitForExit();Check(process.ExitCode==0,"FFmpeg still");
 var oldPixels=older.Quantize(rgb);var newPixels=newer.Quantize(rgb);
 var oldRam=Pack(oldPixels);var newRam=Pack(newPixels);
 var oldScreen=CgaNtsc.Render(oldRam,new());var newScreen=CgaNtsc.Render(newRam,new());
 double oe=Error(Reference(rgb,8),Actual(oldScreen,8)),ne=Error(Reference(rgb,8),Actual(newScreen,8));
 double of=Error(Reference(rgb,2),Actual(oldScreen,2)),nf=Error(Reference(rgb,2),Actual(newScreen,2));
 oldMean+=oe;newMean+=ne;oldFine+=of;newFine+=nf;
 stills.Add(new{Time=time,OldError=oe,NewError=ne,OldDetailError=of,NewDetailError=nf,ReductionPercent=100*(1-ne/oe)});
 using var source=FrameImage.FromRgb(rgb,160,200);using var a=FrameImage.Create(oldRam,1,new());using var b=FrameImage.Create(newRam,1,new());
 using var board=new Bitmap(1920,520);using var graphics=Graphics.FromImage(board);graphics.Clear(Color.FromArgb(242,245,248));
 using var font=new Font("Segoe UI",14);
 graphics.DrawString($"Source / MIR {time}s",font,Brushes.Black,12,8);graphics.DrawString("0.5 / palette quantization + NTSC display",font,Brushes.Black,652,8);graphics.DrawString("0.6 / NTSC-aware quantization + NTSC display",font,Brushes.Black,1292,8);
 graphics.DrawImage(source,new Rectangle(0,40,640,480));graphics.DrawImage(a,640,40);graphics.DrawImage(b,1280,40);board.Save(Path.Combine(output,$"compare-{time}.png"));
 Check(newPixels.SequenceEqual(newer.Quantize(rgb)),"Identical frame is stable");
 Console.WriteLine($"Still {time}: {oe:F6} -> {ne:F6} ({100*(1-ne/oe):F1}%), detail {of:F6} -> {nf:F6}");
}
var motion=new List<object>();
foreach(int time in new[]{30,80,150}) {
 using var process=Decode(time,90);var rgb=new byte[96000];
 var oldEncoder=new NativeEncoder(1,200,30,150,735,75);var newEncoder=new NativeEncoder(1,200,30,150,735,75);
 double oe=0,ne=0,ot=0,nt=0;int samples=0;ColorMixer.Rgb[]? prevTarget=null,prevOld=null,prevNew=null;
 for(int frame=0;frame<90;frame++) {
  process.StandardOutput.BaseStream.ReadExactly(rgb);oldEncoder.Encode(older.Quantize(rgb));newEncoder.Encode(newer.Quantize(rgb));
  if(frame%3!=0)continue;
  var target=Reference(rgb,8);var oldScreen=Actual(CgaNtsc.Render(oldEncoder.Reconstructed,new()),8);var newScreen=Actual(CgaNtsc.Render(newEncoder.Reconstructed,new()),8);
  oe+=Error(target,oldScreen);ne+=Error(target,newScreen);samples++;
  if(prevTarget!=null) for(int p=0;p<target.Length;p++) {
   var change=target[p]-prevTarget[p];ot+=ColorMixer.Distance(oldScreen[p]-prevOld![p],change)/target.Length;nt+=ColorMixer.Distance(newScreen[p]-prevNew![p],change)/target.Length;
  }
  prevTarget=target;prevOld=oldScreen;prevNew=newScreen;
 }
 process.WaitForExit();Check(process.ExitCode==0,"FFmpeg motion");
 motion.Add(new{Start=time,Frames=90,Samples=samples,OldError=oe/samples,NewError=ne/samples,OldTemporalError=ot/(samples-1),NewTemporalError=nt/(samples-1),OldEncoder=oldEncoder.Statistics,NewEncoder=newEncoder.Statistics});
 Check(newEncoder.Statistics.MaximumCycles<=newEncoder.Statistics.CycleBudget,"8088 budget");
 Console.WriteLine($"Motion {time}: {oe/samples:F6} -> {ne/samples:F6}; temporal {ot/(samples-1):F6} -> {nt/(samples-1):F6}; accepted {oldEncoder.Statistics.SourceFramesAccepted}/{newEncoder.Statistics.SourceFramesAccepted}");
}
// Contract checks cover both card revisions, controls and fixed screen phase.
foreach(bool late in new[]{false,true}) {
 var job=new Job{CompositeNewCga=late};var neutral=Enumerable.Repeat((byte)96,96000).ToArray();
 var normal=new FrameWriter(job).Quantize(neutral);
 Check(new FrameWriter(job).Quantize(new byte[96000]).All(v=>v==0),"Black endpoint");
 Check(new FrameWriter(job).Quantize(Enumerable.Repeat((byte)255,96000).ToArray()).All(v=>v==15),"White endpoint");
 job.DitherStrength=0;var zero=new FrameWriter(job).Quantize(neutral);job.DitherStrength=100;job.Dither="none";
 Check(zero.SequenceEqual(new FrameWriter(job).Quantize(neutral)) && zero.Distinct().Count()==1,"Zero strength equals disabled");
 job.Dither="bayer";job.DitherStrength=40;
 var partial=new FrameWriter(job).Quantize(neutral);Check(!partial.SequenceEqual(normal)&&!partial.SequenceEqual(zero),"Intermediate strength");
 job.DitherStrength=100;job.Gamma=1.8;
 double Bright(byte[] px)=>CgaNtsc.Render(Pack(px),new(NewCga:late)).Average(v=>{var c=ColorMixer.FromColor(v&0xffffff);return .2126*c.R+.7152*c.G+.0722*c.B;});
 Check(Bright(new FrameWriter(job).Quantize(neutral))>Bright(normal),"Gamma lifts brightness");
 var before=MonitorSettings.Current;MonitorSettings.Set(new(NewCga:!late,Hue:25,Saturation:.3),false);
 Check(normal.SequenceEqual(new FrameWriter(new Job{CompositeNewCga=late}).Quantize(neutral)),"Preview tuning cannot alter encoded bytes");MonitorSettings.Set(before,false);
 var halfJob=new Job{CompositeNewCga=late,HalfHeight=true};var half=new FrameWriter(halfJob).Quantize(Enumerable.Repeat((byte)128,48000).ToArray());var ram=new byte[16384];NativeEncoder.PackPixels(half,1,100,ram);
 Check(ram.AsSpan(0,8000).ToArray().All(v=>v==0)&&ram.AsSpan(8192,8000).ToArray().Any(v=>v!=0),"Half-height uses only odd bank");
}
using(var cancelled=new CancellationTokenSource()) {cancelled.Cancel();bool caught=false;try{new FrameWriter(new Job{DitherStrength=71},cancelled.Token);}catch(OperationCanceledException){caught=true;}Check(caught,"Cancelled table build");}
Check(new FrameWriter(new Job{DitherStrength=71}).Quantize(new byte[96000]).All(v=>v==0),"Cancellation leaves no partial cached table");
foreach(string value in new[]{"", "bogus"}){bool caught=false;try{new Job{CompositeAlgorithm=value}.ValidateColor();}catch(ArgumentException){caught=true;}Check(caught,"Invalid algorithm rejected");}
var lateWriter=new FrameWriter(new Job{CompositeNewCga=true});double lateOld=0,lateNew=0;
foreach(int seconds in new[]{30,50,80,110,150,180}) {
 using var proc=Decode(seconds,1);var rgb=new byte[96000];proc.StandardOutput.BaseStream.ReadExactly(rgb);proc.WaitForExit();Check(proc.ExitCode==0,"Late CGA source decode");
 var target=Reference(rgb,8);lateOld+=Error(target,Actual(CgaNtsc.Render(Pack(older.Quantize(rgb)),new(NewCga:true)),8));lateNew+=Error(target,Actual(CgaNtsc.Render(Pack(lateWriter.Quantize(rgb)),new(NewCga:true)),8));
}
Check(lateNew<lateOld,"Late CGA matching improves model colour error");
var fullMotion=new List<object>();
string oldFile=Path.Combine(root,"local/reference/0.5/MIR_CGA.XDV"),newFile=Path.Combine(output,"integration/MIR_CGA.XDV");
if(File.Exists(oldFile)&&File.Exists(newFile)) {
 using var oldReader=new XdvReader(oldFile);using var newReader=new XdvReader(newFile);using var proc=Decode(0,6042);var rgb=new byte[96000];
 double oldError=0,newError=0,oldDetail=0,newDetail=0;int sampled=0;
 var dense=new Dictionary<int,(double Old,double New,double OldTemporal,double NewTemporal,int Count)>();
 var previous=new Dictionary<int,(ColorMixer.Rgb[] Target,ColorMixer.Rgb[] Old,ColorMixer.Rgb[] New)>();
 for(int frame=0;frame<6042;frame++) {
  proc.StandardOutput.BaseStream.ReadExactly(rgb);
  int segment=new[]{30,80,150}.FirstOrDefault(t=>frame>=t*30&&frame<(t+3)*30);
  if(frame%30!=0&&segment==0)continue;
  var target=Reference(rgb,8);var oldScreen=CgaNtsc.Render(oldReader.ReadFrame(frame),new());var newScreen=CgaNtsc.Render(newReader.ReadFrame(frame),new());
  var oldPatch=Actual(oldScreen,8);var newPatch=Actual(newScreen,8);double oe=Error(target,oldPatch),ne=Error(target,newPatch);
  if(segment!=0 && frame==segment*30) {
   using var source=FrameImage.FromRgb(rgb,160,200);using var oldImage=FrameImage.Create(oldReader.ReadFrame(frame),1,new());using var newImage=FrameImage.Create(newReader.ReadFrame(frame),1,new());
   using var board=new Bitmap(1920,520);using var g=Graphics.FromImage(board);g.Clear(Color.FromArgb(242,245,248));using var font=new Font("Segoe UI",14);
   g.DrawString($"Source scaled to 160 x 200 / {segment}s",font,Brushes.Black,12,8);g.DrawString("Old XDV / NTSC display",font,Brushes.Black,652,8);g.DrawString("New XDV / NTSC display",font,Brushes.Black,1292,8);
   g.DrawImage(source,new Rectangle(0,40,640,480));g.DrawImage(oldImage,640,40);g.DrawImage(newImage,1280,40);board.Save(Path.Combine(output,$"encoded-compare-{segment}.png"));
  }
  if(frame%30==0){oldError+=oe;newError+=ne;var detail=Reference(rgb,2);oldDetail+=Error(detail,Actual(oldScreen,2));newDetail+=Error(detail,Actual(newScreen,2));sampled++;}
  if(segment!=0) {
   dense.TryGetValue(segment,out var acc);acc.Old+=oe;acc.New+=ne;acc.Count++;
   if(previous.TryGetValue(segment,out var prev)) for(int p=0;p<target.Length;p++) {
    var change=target[p]-prev.Target[p];acc.OldTemporal+=ColorMixer.Distance(oldPatch[p]-prev.Old[p],change)/target.Length;acc.NewTemporal+=ColorMixer.Distance(newPatch[p]-prev.New[p],change)/target.Length;
   }
   dense[segment]=acc;previous[segment]=(target,oldPatch,newPatch);
  }
 }
 proc.WaitForExit();Check(proc.ExitCode==0,"Full MIR reference decode");
 foreach(var item in dense){var v=item.Value;fullMotion.Add(new{Start=item.Key,Frames=v.Count,OldError=v.Old/v.Count,NewError=v.New/v.Count,OldTemporalError=v.OldTemporal/(v.Count-1),NewTemporalError=v.NewTemporal/(v.Count-1)});}
 var report=new{Frames=6042,SampledFrames=sampled,OldError=oldError/sampled,NewError=newError/sampled,ReductionPercent=100*(1-newError/oldError),OldDetailError=oldDetail/sampled,NewDetailError=newDetail/sampled,DetailReductionPercent=100*(1-newDetail/oldDetail),Motion=fullMotion};
 Check(newError<oldError&&newDetail<oldDetail,"Full encoded MIR improves colour and detail error");
 File.WriteAllText(Path.Combine(output,"encoded-quality.json"),JsonSerializer.Serialize(report,Data.Json));Console.WriteLine($"Full MIR: {100*(1-newError/oldError):F1}% colour error reduction; {100*(1-newDetail/oldDetail):F1}% detail error reduction");
}
Check(newMean<oldMean&&newFine<oldFine,"Still colour and detail error improve");
var result=new{Success=true,Checks=checks,ColdTableSeconds=cold,LateCgaReductionPercent=100*(1-lateNew/lateOld),StillReductionPercent=100*(1-newMean/oldMean),DetailReductionPercent=100*(1-newFine/oldFine),Stills=stills,Motion=motion};
File.WriteAllText(Path.Combine(output,"quality.json"),JsonSerializer.Serialize(result,Data.Json));
