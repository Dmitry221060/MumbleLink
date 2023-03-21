using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

[assembly: ModInfo("MumbleLink",
	Description = "Enables Mumble positional audio support through its Link plugin",
	Website = "https://github.com/copygirl/MumbleLink",
	Authors = new []{ "copygirl", "Nikky" },
	Version = "1.3.0", Side = "Client")]

namespace MumbleLink
{
	public class MumbleLinkSystem : ModSystem, IDisposable
	{
		private ICoreClientAPI _api;
		private MemoryMappedFile _mappedFile;
		private MemoryMappedViewStream _stream;
		private FileSystemWatcher _watcher;
		
		private readonly MumbleLinkData _data = new();
		
		public override void StartClientSide(ICoreClientAPI api)
		{
			_api = api;
			api.Event.RegisterGameTickListener(OnGameTick, 20);
			
			if (Environment.OSVersion.Platform == PlatformID.Unix) {
				var fileName = $"/dev/shm/MumbleLink.{getuid()}";
				
				void OnCreated(object sender, FileSystemEventArgs e)
				{
					Mod.Logger.Notification("Link established");
					_mappedFile = MemoryMappedFile.CreateFromFile(fileName);
					_stream     = _mappedFile.CreateViewStream(0, MumbleLinkData.Size);
				}
				void OnDeleted(object sender, FileSystemEventArgs e)
				{
					Mod.Logger.Notification("Link lost");
					_stream.Dispose();
					_mappedFile.Dispose();
					_stream     = null;
					_mappedFile = null;
				}
				
				if (File.Exists(fileName))
					OnCreated(null, null);
				
				_watcher = new FileSystemWatcher(Path.GetDirectoryName(fileName), Path.GetFileName(fileName));
				_watcher.Created += OnCreated;
				_watcher.Deleted += OnDeleted;
				_watcher.EnableRaisingEvents = true;
			} else {
				_mappedFile = MemoryMappedFile.CreateOrOpen("MumbleLink", MumbleLinkData.Size);
				_stream     = _mappedFile.CreateViewStream(0, MumbleLinkData.Size);
			}
		}
		
		private void OnGameTick(float delta)
		{
			if ((_stream == null) || (_api.World?.Player == null) || _api.IsSinglePlayer) return;
			_data.UITick++;
			
			_data.Context  = _api.World.Seed.ToString();
			_data.Identity = _api.World.Player.PlayerUID;
			
			var player = _api.World.Player;
			var entity = player.Entity;
			
			// Mumble Link uses left-handed coordinate system (+X is to the right)
			// wheras Vintage Story uses a right-handed one (where +X is to the left),
			// so we actually have the flip the X coordinate to get the right values.
			static Vec3d FlipX(Vec3d vec) => new(-vec.X, vec.Y, vec.Z);
			
			var headPitch = entity.Pos.HeadPitch;
			var headYaw   = entity.Pos.Yaw + entity.Pos.HeadYaw;
			_data.AvatarPosition = FlipX(entity.Pos.XYZ + entity.LocalEyePos);
			_data.AvatarFront = new Vec3d(
				-GameMath.Cos(headYaw) * GameMath.Cos(headPitch),
				-GameMath.Sin(headPitch),
				-GameMath.Sin(headYaw) * GameMath.Cos(headPitch));
			
			_data.CameraPosition = FlipX(entity.CameraPos);
			_data.CameraFront = _data.AvatarFront;
			
			_stream.Position = 0;
			_data.Write(_stream);
		}
		
		public override void Dispose()
		{
			_watcher?.Dispose();
			_stream?.Dispose();
			_mappedFile?.Dispose();
		}
		
		[DllImport("libc")]
		private static extern uint getuid();
	}
}
