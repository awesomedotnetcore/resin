﻿using System;
using System.Collections.Generic;
using System.IO;

namespace Sir.Store
{
    public class PagedPostingsWriter
    {
        public static int PAGE_SIZE = 4096;
        public static int BLOCK_SIZE = sizeof(ulong) + sizeof(byte);
        public static int SLOTS_PER_PAGE = (PAGE_SIZE / BLOCK_SIZE);

        private readonly Stream _stream;
        private readonly byte[] _pageBuf;
        private readonly byte[] _aliveStatus;
        private readonly byte[] _deadStatus;

        public PagedPostingsWriter(Stream stream)
        {
            _stream = stream;
            _pageBuf = new byte[PAGE_SIZE];
            _aliveStatus = new byte[1];
            _deadStatus = new byte[1];

            _aliveStatus[0] = 1;
            _deadStatus[0] = 0;
        }

        public long AllocatePage()
        {
            _stream.Seek(0, SeekOrigin.End);

            var pos = _stream.Position;

            _stream.SetLength(_stream.Position + PAGE_SIZE);

            return pos;
        }

        public void FlagAsDeleted(long pageOffset, ulong docId)
        {
            if (_stream.Position != pageOffset)
            {
                _stream.Seek(pageOffset, SeekOrigin.Begin);
            }

            _stream.Read(_pageBuf, 0, PAGE_SIZE);

            var blockBuf = new byte[BLOCK_SIZE];
            int pos = 0;
            ulong id = 0;
            bool notFound = false;

            while (pos < SLOTS_PER_PAGE)
            {
                id = BitConverter.ToUInt64(_pageBuf, pos * (BLOCK_SIZE));

                if (id == 0)
                {
                    notFound = true;
                    break;
                }
                else if (id == docId)
                {
                    break;
                }
                else
                {
                    pos++;
                }
            }

            if (notFound)
            {
                if (pos == SLOTS_PER_PAGE)
                {
                    // Page is full but luckily the last word 
                    // is the offset for the next page.
                    // Jump to that location and continue reading there.

                    long nextOffset = Convert.ToInt64(id);
                    FlagAsDeleted(nextOffset, docId);
                }
            }
            else
            {
                var offsetToFlag = pageOffset + (pos * (BLOCK_SIZE)) + sizeof(ulong);

                _stream.Seek(offsetToFlag, SeekOrigin.Begin);
                _stream.Write(_deadStatus, 0, sizeof(byte));

                _stream.Seek(pageOffset, SeekOrigin.Begin);
                _stream.Read(_pageBuf, 0, PAGE_SIZE);
            }
        }

        private void Write(long offset, ulong docId)
        {
            if (_stream.Position != offset)
            {
                _stream.Seek(offset, SeekOrigin.Begin);
            }

            _stream.Read(_pageBuf, 0, PAGE_SIZE);

            int pos = 0;
            ulong id = 0;

            while (pos < SLOTS_PER_PAGE)
            {
                id = BitConverter.ToUInt64(_pageBuf, pos * (BLOCK_SIZE));

                if (id == 0)
                {
                    // Zero means "no data". We can start writing into the page at the current position.
                    break;
                }

                pos++;
            }

            if (pos == SLOTS_PER_PAGE)
            {
                // Page is full but luckily the last word 
                // is the offset for the next page.
                // Jump to that location and continue writing there.

                long nextOffset = Convert.ToInt64(id);
                Write(nextOffset, docId);
            }
            else if (pos == SLOTS_PER_PAGE - 2)
            {
                // Only two slots left in the page.
                // Fill the penultimate with a docId, 
                // allocate a new page and
                // store the offset of the new page
                // in the last slot.

                var posOfPenultimate = offset + (pos * BLOCK_SIZE);

                _stream.Seek(posOfPenultimate, SeekOrigin.Begin);
                _stream.Write(BitConverter.GetBytes(docId), 0, sizeof(ulong));
                _stream.Write(_aliveStatus, 0, sizeof(byte));

                var nextOffset = AllocatePage();
                var posOfUltimate = posOfPenultimate + BLOCK_SIZE;

                _stream.Seek(posOfUltimate, SeekOrigin.Begin);

                _stream.Write(BitConverter.GetBytes(nextOffset), 0, sizeof(long));
            }
            else
            {
                // There was a vacant slot in the page.

                var o = offset + (pos * BLOCK_SIZE);

                _stream.Seek(o, SeekOrigin.Begin);
                _stream.Write(BitConverter.GetBytes(docId), 0, sizeof(ulong));
                _stream.Write(_aliveStatus, 0, sizeof(byte));
            }
        }

        public void Write(long offset, IList<ulong> docIds, int docIndex)
        {
            if (_stream.Position != offset)
            {
                _stream.Seek(offset, SeekOrigin.Begin);
            }

            _stream.Read(_pageBuf, 0, PAGE_SIZE);

            int pos = 0;
            ulong id = 0;

            while (pos < SLOTS_PER_PAGE)
            {
                id = BitConverter.ToUInt64(_pageBuf, pos * (BLOCK_SIZE));

                if (id == 0)
                {
                    // Zero means "no data". We can start writing into the page at the current position.
                    break;
                }

                pos++;
            }

            if (pos == SLOTS_PER_PAGE)
            {
                // Page is full but luckily the last word 
                // is the offset for the next page.
                // Jump to that location and continue writing there.

                long nextOffset = Convert.ToInt64(id);

                Write(nextOffset, docIds, docIndex);
            }
            else if (pos == SLOTS_PER_PAGE - 2)
            {
                // Only two slots left in the page.
                // Fill the penultimate with a docId, 
                // allocate a new page and
                // store the offset of the new page
                // in the last slot.

                var posOfPenultimate = offset + (pos * BLOCK_SIZE);

                _stream.Seek(posOfPenultimate, SeekOrigin.Begin);
                _stream.Write(BitConverter.GetBytes(docIds[docIndex++]), 0, sizeof(ulong));
                _stream.Write(_aliveStatus, 0, sizeof(byte));

                var nextOffset = AllocatePage();
                var posOfUltimate = posOfPenultimate + BLOCK_SIZE;

                _stream.Seek(posOfUltimate, SeekOrigin.Begin);
                _stream.Write(BitConverter.GetBytes(nextOffset), 0, sizeof(long));

                if (docIds.Count > docIndex)
                {
                    Write(offset, docIds, docIndex);
                }
            }
            else
            {
                // There were vacant slots in the page.

                var offs = offset + (pos * BLOCK_SIZE);

                while (pos++ < SLOTS_PER_PAGE - 2)
                {
                    if (docIndex == docIds.Count) break;

                    _stream.Seek(offs, SeekOrigin.Begin);
                    _stream.Write(BitConverter.GetBytes(docIds[docIndex++]), 0, sizeof(ulong));
                    _stream.Write(_aliveStatus, 0, sizeof(byte));

                    offs += BLOCK_SIZE;
                }

                if (docIds.Count > docIndex)
                {
                    Write(offset, docIds, docIndex);
                }    
            }
        }

        public long Write(IList<ulong> docIds)
        {
            var offset = AllocatePage();

            Write(offset, docIds, 0);

            return offset;

        }
    }
}
