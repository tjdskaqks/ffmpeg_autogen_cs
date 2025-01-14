﻿using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Example;
using System;
using System.IO;

namespace encode_audio
{
    internal unsafe class Program
    {
        static void Main(string[] args)
        {
            FFmpegBinariesHelper.RegisterFFmpegBinaries();

#if DEBUG
            Console.WriteLine("Current directory: " + Environment.CurrentDirectory);
            Console.WriteLine("Running in {0}-bit mode.", Environment.Is64BitProcess ? "64" : "32");
            Console.WriteLine($"FFmpeg version info: {ffmpeg.av_version_info()}");
#endif
            Console.WriteLine();

            Console.WriteLine($"LIBAVFORMAT Version: {ffmpeg.LIBAVFORMAT_VERSION_MAJOR}.{ffmpeg.LIBAVFORMAT_VERSION_MINOR}");

            string dirPath = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? "";
            string outputFilePath = Path.Combine(dirPath, "test.mp2");
            encode_audio(outputFilePath);
        }

        private static void encode_audio(string filename)
        {
            AVCodec* codec;
            AVCodecContext* c = null;
            AVFrame* frame = null;
            AVPacket* pkt = null;
            int i, j, k, ret;
            short* samples;
            float t, tincr;

            /* find the MP2 encoder */
            codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_MP2);
            if (codec == null)
            {
                Console.WriteLine("Codec not found");
                return;
            }

            do
            {
                c = ffmpeg.avcodec_alloc_context3(codec);
                if (c == null)
                {
                    Console.WriteLine("Could not allocate audio codec context");
                    break;
                }

                /* put sample parameters */
                c->bit_rate = 64000;

                /* check that the encoder supports s16 pcm input */
                c->sample_fmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
                if (check_sample_fmt(codec, c->sample_fmt) == 0)
                {
                    Console.WriteLine($"Encoder does not support sample format {ffmpeg.av_get_sample_fmt_name(c->sample_fmt)}");
                    break;
                }

                /* select other audio parameters supported by the encoder */
                c->sample_rate = select_sample_rate(codec);
                c->channel_layout = select_channel_layout(codec);
                c->channels = ffmpeg.av_get_channel_layout_nb_channels(c->channel_layout);

                /* open it */
                if (ffmpeg.avcodec_open2(c, codec, null) < 0)
                {
                    Console.WriteLine("Could not open codec");
                    break;
                }

                using FileStream f = System.IO.File.OpenWrite(filename);

                /* packet for holding encoded output */
                pkt = ffmpeg.av_packet_alloc();
                if (pkt == null)
                {
                    Console.WriteLine("could not allocate the packet");
                    break;
                }

                /* frame containing input raw audio */
                frame = ffmpeg.av_frame_alloc();
                if (frame == null)
                {
                    Console.WriteLine("Could not allocate audio frame");
                    break;
                }

                frame->nb_samples = c->frame_size;
                frame->format = (int)c->sample_fmt;
                frame->channel_layout = c->channel_layout;

                /* allocate the data buffers */
                ret = ffmpeg.av_frame_get_buffer(frame, 0);
                if (ret < 0)
                {
                    Console.WriteLine("Could not allocate audio data buffers");
                    break;
                }

                /* encode a single tone sound */
                t = 0;
                tincr = (float)(2 * Math.PI * 440.0 / c->sample_rate);
                for (i = 0; i < 200; i++)
                {
                    /* make sure the frame is writable -- makes a copy if the encoder
                     * kept a reference internally */
                    ret = ffmpeg.av_frame_make_writable(frame);
                    if (ret < 0)
                    {
                        break;
                    }

                    samples = (short*)frame->data[0];

                    for (j = 0; j < c->frame_size; j++)
                    {
                        samples[2 * j] = (short)(Math.Sin(t) * 10000);

                        for (k = 1; k < c->channels; k++)
                        {
                            samples[2 * j + k] = samples[2 * j];
                        }

                        t += tincr;
                    }
                    encode(c, frame, pkt, f);
                }

                /* flush the encoder */
                encode(c, null, pkt, f);

            } while (false);

            if (frame != null)
            {
                ffmpeg.av_frame_free(&frame);
            }

            if (pkt != null)
            {
                ffmpeg.av_packet_free(&pkt);
            }

            if (c != null)
            {
                ffmpeg.avcodec_free_context(&c);
            }
        }

        /* check that a given sample format is supported by the encoder */
        static unsafe int check_sample_fmt(AVCodec* codec, AVSampleFormat sample_fmt)
        {
            AVSampleFormat* p = codec->sample_fmts;

            while (*p != AVSampleFormat.AV_SAMPLE_FMT_NONE)
            {
                if (*p == sample_fmt)
                {
                    return 1;
                }

                p++;
            }

            return 0;
        }

        /* just pick the highest supported samplerate */
        static unsafe int select_sample_rate(AVCodec* codec)
        {
            int* p;
            int best_samplerate = 0;

            if (codec->supported_samplerates == null)
            {
                return 44100;
            }

            p = codec->supported_samplerates;
            while (*p != 0)
            {
                if (best_samplerate == 0 || Math.Abs(44100 - *p) < Math.Abs(44100 - best_samplerate))
                {
                    best_samplerate = *p;
                }

                p++;
            }

            return best_samplerate;
        }

        /* select layout with the highest channel count */
        static unsafe ulong select_channel_layout(AVCodec* codec)
        {
            ulong* p;
            ulong best_ch_layout = 0;
            int best_nb_channels = 0;

            if (codec->channel_layouts == null)
            {
                return ffmpeg.AV_CH_LAYOUT_STEREO;
            }

            p = codec->channel_layouts;
            while (*p != 0)
            {
                int nb_channels = ffmpeg.av_get_channel_layout_nb_channels(*p);

                if (nb_channels > best_nb_channels)
                {
                    best_ch_layout = *p;
                    best_nb_channels = nb_channels;
                }

                p++;
            }

            return best_ch_layout;
        }

        static bool encode(AVCodecContext* ctx, AVFrame* frame, AVPacket* pkt, FileStream output)
        {
            int ret;

            /* send the frame for encoding */
            ret = ffmpeg.avcodec_send_frame(ctx, frame);
            if (ret < 0)
            {
                Console.WriteLine("Error sending the frame to the encoder");
                return false;
            }

            /* read all the available output packets (in general there may be any
             * number of them */
            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(ctx, pkt);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF)
                {
                    return true;
                }
                else if (ret < 0)
                {
                    Console.WriteLine("Error encoding audio frame");
                    return false;
                }

                ReadOnlySpan<byte> buffer = new ReadOnlySpan<byte>(pkt->data, pkt->size);
                output.Write(buffer);
                ffmpeg.av_packet_unref(pkt);
            }

            return true;
        }
    }
}