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

using System.Linq;
using VDF.Core;

namespace VDF.Core.Utils {
	internal static class FilenameSequence {
		static readonly char[] PrefixTrimChars = [' ', '_', '-', '.', '(', ')', '[', ']', '{', '}'];

		internal static Dictionary<string, int> BuildSequenceAffinityIndex(IEnumerable<string> paths) {
			var sequences = new Dictionary<string, HashSet<int>>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			foreach (var path in paths) {
				if (!TryParseSequenceKey(path, out string? key, out int number))
					continue;

				if (!sequences.TryGetValue(key, out var numbers)) {
					numbers = new HashSet<int>();
					sequences[key] = numbers;
				}
				numbers.Add(number);
			}

			var scores = new Dictionary<string, int>(
				CoreUtils.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

			foreach (var path in paths) {
				if (!TryParseSequenceKey(path, out string? key, out int number))
					continue;

				var numbers = sequences[key];
				int score = 0;
				if (numbers.Contains(number - 1))
					score++;
				if (numbers.Contains(number + 1))
					score++;
				scores[path] = score;
			}

			return scores;
		}

		internal static bool TryParseSequenceKey(string path, out string key, out int number) {
			key = string.Empty;
			number = 0;

			string stem = Path.GetFileNameWithoutExtension(path).Trim();
			if (stem.Length < 2)
				return false;

			int end = stem.Length - 1;
			if (!char.IsDigit(stem[end]))
				return false;

			int start = end;
			while (start >= 0 && char.IsDigit(stem[start]))
				start--;

			int numberStart = start + 1;
			if (numberStart <= 0 || numberStart >= stem.Length)
				return false;

			string prefix = stem[..numberStart].TrimEnd(PrefixTrimChars);
			if (prefix.Length == 0)
				return false;

			if (!int.TryParse(stem[numberStart..], out number))
				return false;

			key = prefix;
			return true;
		}

		internal static IReadOnlyList<FileEntry> FindAdjacentSequenceItems(FileEntry selected, IEnumerable<FileEntry> database) {
			if (!TryParseSequenceKey(selected.Path, out string selectedKey, out int selectedNumber))
				return Array.Empty<FileEntry>();

			string selectedExtension = Path.GetExtension(selected.Path);
			bool selectedIsImage = selected.IsImage;

			var sameSeries = database
				.Where(entry =>
					!entry.Path.Equals(selected.Path, CoreUtils.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
					entry.IsImage == selectedIsImage &&
					Path.GetExtension(entry.Path).Equals(selectedExtension, StringComparison.OrdinalIgnoreCase) &&
					TryParseSequenceKey(entry.Path, out string entryKey, out _) &&
					string.Equals(entryKey, selectedKey, StringComparison.OrdinalIgnoreCase))
				.Select(entry => {
					TryParseSequenceKey(entry.Path, out _, out int entryNumber);
					return new { Entry = entry, Number = entryNumber, Score = GetMetadataDistance(selected, entry) };
				})
				.ToList();

			if (sameSeries.Count == 0)
				return Array.Empty<FileEntry>();

			FileEntry? PickNeighbor(int targetNumber, bool lowerSide) {
				var candidates = sameSeries
					.Where(x => lowerSide ? x.Number < selectedNumber && x.Number >= targetNumber
					                     : x.Number > selectedNumber && x.Number <= targetNumber)
					.GroupBy(x => x.Number)
					.Select(g => g.OrderBy(x => x.Score).ThenBy(x => x.Entry.Path).First())
					.OrderByDescending(x => lowerSide ? x.Number : -x.Number)
					.ThenBy(x => x.Score)
					.ToList();
				return candidates.FirstOrDefault()?.Entry;
			}

			int minNumber = sameSeries.Min(x => x.Number);
			int maxNumber = sameSeries.Max(x => x.Number);
			FileEntry? previous = PickNeighbor(selectedNumber - 1, lowerSide: true)
				?? PickNeighbor(minNumber, lowerSide: true);
			FileEntry? next = PickNeighbor(selectedNumber + 1, lowerSide: false)
				?? PickNeighbor(maxNumber, lowerSide: false);

			var result = new List<FileEntry>(2);
			if (previous != null)
				result.Add(previous);
			if (next != null && !string.Equals(next.Path, previous?.Path, StringComparison.OrdinalIgnoreCase))
				result.Add(next);
			return result;
		}

		static double GetMetadataDistance(FileEntry selected, FileEntry candidate) {
			double durationA = selected.mediaInfo?.Duration.TotalSeconds ?? 0d;
			double durationB = candidate.mediaInfo?.Duration.TotalSeconds ?? 0d;
			double durationScore = Math.Abs(durationA - durationB) * 1000d;

			(int width, int height) selectedSize = GetVideoSize(selected);
			(int width, int height) candidateSize = GetVideoSize(candidate);
			double frameScore = Math.Abs(selectedSize.width - candidateSize.width) + Math.Abs(selectedSize.height - candidateSize.height);

			double fpsScore = Math.Abs(GetVideoFps(selected) - GetVideoFps(candidate)) * 100d;
			double bitRateScore = Math.Abs(GetVideoBitRate(selected) - GetVideoBitRate(candidate)) / 1000d;
			double sizeScore = Math.Abs(selected.FileSize - candidate.FileSize) / 1_000_000d;
			double videoCodecScore = StringMismatchPenalty(GetVideoCodec(selected), GetVideoCodec(candidate), 5_000d);
			double audioCodecScore = StringMismatchPenalty(GetAudioCodec(selected), GetAudioCodec(candidate), 3_000d);
			double audioLayoutScore = StringMismatchPenalty(GetAudioChannelLayout(selected), GetAudioChannelLayout(candidate), 2_000d);
			double audioSampleRateScore = Math.Abs(GetAudioSampleRate(selected) - GetAudioSampleRate(candidate)) * 2d;
			double audioBitRateScore = Math.Abs(GetAudioBitRate(selected) - GetAudioBitRate(candidate)) / 1000d;
			double hdrScore = Math.Abs(GetHdrRank(selected) - GetHdrRank(candidate)) * 1_500d;

			return durationScore + frameScore + fpsScore + bitRateScore + sizeScore +
				videoCodecScore + audioCodecScore + audioLayoutScore + audioSampleRateScore + audioBitRateScore + hdrScore;
		}

		static (int width, int height) GetVideoSize(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));
			return stream == null ? (0, 0) : (stream.Width, stream.Height);
		}

		static float GetVideoFps(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));
			return stream?.FrameRate ?? 0f;
		}

		static long GetVideoBitRate(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));
			return stream?.BitRate ?? 0;
		}

		static string GetVideoCodec(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));
			return NormalizeMetaValue(stream?.CodecName);
		}

		static string GetAudioCodec(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));
			return NormalizeMetaValue(stream?.CodecName);
		}

		static string GetAudioChannelLayout(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));
			return NormalizeMetaValue(stream?.ChannelLayout);
		}

		static int GetAudioSampleRate(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));
			return stream?.SampleRate ?? 0;
		}

		static long GetAudioBitRate(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "audio", StringComparison.OrdinalIgnoreCase));
			return stream?.BitRate ?? 0;
		}

		static int GetHdrRank(FileEntry entry) {
			var stream = entry.mediaInfo?.Streams?.FirstOrDefault(s =>
				string.Equals(s.CodecType, "video", StringComparison.OrdinalIgnoreCase));
			return stream?.HdrFormat switch {
				"Dolby Vision" => 3,
				"Hdr10+" => 2,
				"Hdr10" => 1,
				"Hdr" => 1,
				_ => 0,
			};
		}

		static double StringMismatchPenalty(string left, string right, double penalty) {
			if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
				return 0d;
			return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ? 0d : penalty;
		}

		static string NormalizeMetaValue(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}
}
