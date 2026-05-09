// /*
//     Copyright (C) 2026 0x90d
//     This file is part of VideoDuplicateFinder
//     VideoDuplicateFinder is free software: you can redistribute it and/or modify
//     it under the terms of the GNU Affero General Public License as published by
//     the Free Software Foundation, either version 3 of the License, or
//     (at your option) any later version.
//     VideoDuplicateFinder is distributed in the hope that it will be useful,
//     but WITHOUT ANY WARRANTY; without even the implied warranty of
//     MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//     GNU Affero General Public License for more details.
//     You should have received a copy of the GNU Affero General Public License
//     along with VideoDuplicateFinder.  If not, see <http://www.gnu.org/licenses/>.
// */
//

using VDF.Core;
using VDF.Core.Utils;

namespace VDF.Core.Tests.Utils;

public class FilenameSequenceTests {
	static FileEntry MakeEntry(
		string path,
		double durationSeconds,
		int width,
		int height,
		long bitRate = 1_000_000,
		string videoCodec = "h264",
		string audioCodec = "aac",
		string audioLayout = "stereo",
		int audioSampleRate = 48_000,
		long audioBitRate = 128_000) {
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, "x");
		var entry = new FileEntry(path) {
			mediaInfo = new MediaInfo {
				Duration = TimeSpan.FromSeconds(durationSeconds),
				Streams = new[] {
					new MediaInfo.StreamInfo {
						CodecType = "video",
						CodecName = videoCodec,
						Width = width,
						Height = height,
						BitRate = bitRate,
						FrameRate = 30f
					},
					new MediaInfo.StreamInfo {
						CodecType = "audio",
						CodecName = audioCodec,
						ChannelLayout = audioLayout,
						SampleRate = audioSampleRate,
						BitRate = audioBitRate
					}
				}
			}
		};
		return entry;
	}

	[Fact]
	public void BuildSequenceAffinityIndex_GivesMiddleItemHighestScore() {
		var paths = new[] {
			Path.Combine("folderA", "a1.mp4"),
			Path.Combine("folderB", "a2.mp4"),
			Path.Combine("folderC", "a3.mp4"),
			Path.Combine("folderX", "b2.mp4"),
			Path.Combine("folderY", "movie.mp4"),
		};

		var scores = FilenameSequence.BuildSequenceAffinityIndex(paths);

		Assert.Equal(1, scores[Path.Combine("folderA", "a1.mp4")]);
		Assert.Equal(2, scores[Path.Combine("folderB", "a2.mp4")]);
		Assert.Equal(1, scores[Path.Combine("folderC", "a3.mp4")]);
		Assert.Equal(0, scores[Path.Combine("folderX", "b2.mp4")]);
		Assert.False(scores.ContainsKey(Path.Combine("folderY", "movie.mp4")));
	}

	[Fact]
	public void TryParseSequenceKey_RejectsNamesWithoutTrailingDigits() {
		Assert.False(FilenameSequence.TryParseSequenceKey(Path.Combine("folder", "movie.mp4"), out _, out _));
	}

	[Fact]
	public void FindAdjacentSequenceItems_PicksMatchingMetadataOverSameNameDecoys() {
		var root = Path.Combine(Path.GetTempPath(), "vdf-sequence-tests", Guid.NewGuid().ToString("N"));
		try {
			var selected = MakeEntry(Path.Combine(root, "seriesA", "a2.mp4"), 100, 1920, 1080);
			var a1Good = MakeEntry(Path.Combine(root, "seriesA", "a1.mp4"),  90, 1920, 1080);
			var a3Good = MakeEntry(Path.Combine(root, "seriesA", "a3.mp4"), 110, 1920, 1080);
			var a1Decoy = MakeEntry(Path.Combine(root, "seriesB", "a1.mp4"),  90, 1920, 1080, videoCodec: "hevc", audioCodec: "opus");
			var a3Decoy = MakeEntry(Path.Combine(root, "seriesB", "a3.mp4"), 110, 1920, 1080, videoCodec: "hevc", audioCodec: "opus");
			var db = new[] { selected, a1Good, a3Good, a1Decoy, a3Decoy };

			var related = FilenameSequence.FindAdjacentSequenceItems(selected, db);

			Assert.Equal(2, related.Count);
			Assert.Equal(a1Good.Path, related[0].Path);
			Assert.Equal(a3Good.Path, related[1].Path);
		}
		finally {
			try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
