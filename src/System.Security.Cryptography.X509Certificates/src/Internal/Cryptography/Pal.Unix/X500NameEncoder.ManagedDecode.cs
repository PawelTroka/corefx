// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.Asn1;
using System.Text;

namespace Internal.Cryptography.Pal
{
    internal static partial class X500NameEncoder
    {
        private static string X500DistinguishedNameDecode(
            byte[] encodedName,
            bool printOid,
            bool reverse,
            bool quoteIfNeeded,
            string dnSeparator,
            string multiValueSeparator,
            bool addTrailingDelimiter)
        {
            AsnReader x500NameReader = new AsnReader(encodedName, AsnEncodingRules.DER);
            AsnReader x500NameSequenceReader = x500NameReader.ReadSequence();
            var rdnReaders = new List<AsnReader>();

            x500NameReader.ThrowIfNotEmpty();

            while (x500NameSequenceReader.HasData)
            {
                rdnReaders.Add(x500NameSequenceReader.ReadSetOf());
            }

            // We need to allocate a StringBuilder to hold the data as we're building it, and there's the usual
            // arbitrary process of choosing a number that's "big enough" to minimize reallocations without wasting
            // too much space in the average case.
            //
            // So, let's look at an example of what our output might be.
            //
            // GitHub.com's SSL cert has a "pretty long" subject (partially due to the unknown OIDs):
            //   businessCategory=Private Organization
            //   1.3.6.1.4.1.311.60.2.1.3=US
            //   1.3.6.1.4.1.311.60.2.1.2=Delaware
            //   serialNumber=5157550
            //   street=548 4th Street
            //   postalCode=94107
            //   C=US
            //   ST=California
            //   L=San Francisco
            //   O=GitHub, Inc.
            //   CN=github.com
            //
            // Which comes out to 228 characters using OpenSSL's default pretty-print
            // (openssl x509 -in github.cer -text -noout)
            // Throw in some "maybe-I-need-to-quote-this" quotes, and a couple of extra/extra-long O/OU values
            // and round that up to the next programmer number, and you get that 512 should avoid reallocations
            // in all but the most dire of cases.
            StringBuilder decodedName = new StringBuilder(512);
            int entryCount = rdnReaders.Count;
            bool printSpacing = false;

            for (int i = 0; i < entryCount; i++)
            {
                int loc = reverse ? entryCount - i - 1 : i;

                // RelativeDistinguishedName ::=
                //   SET SIZE (1..MAX) OF AttributeTypeAndValue
                // 
                // AttributeTypeAndValue::= SEQUENCE {
                //   type AttributeType,
                //   value    AttributeValue }
                //
                // AttributeType::= OBJECT IDENTIFIER
                //
                // AttributeValue ::= ANY-- DEFINED BY AttributeType

                if (printSpacing)
                {
                    decodedName.Append(dnSeparator);
                }
                else
                {
                    printSpacing = true;
                }

                AsnReader rdnReader = rdnReaders[loc];
                bool hadValue = false;

                while (rdnReader.HasData)
                {
                    AsnReader tavReader = rdnReader.ReadSequence();
                    string oid = tavReader.ReadObjectIdentifierAsString();
                    string attributeValue = ReadString(tavReader);

                    tavReader.ThrowIfNotEmpty();

                    if (hadValue)
                    {
                        decodedName.Append(multiValueSeparator);
                    }
                    else
                    {
                        hadValue = true;
                    }

                    if (printOid)
                    {
                        AppendOid(decodedName, oid);
                    }


                    bool quote = quoteIfNeeded && NeedsQuoting(attributeValue);

                    if (quote)
                    {
                        decodedName.Append('"');

                        // If the RDN itself had a quote within it, that quote needs to be escaped
                        // with another quote.
                        attributeValue = attributeValue.Replace("\"", "\"\"");
                    }

                    decodedName.Append(attributeValue);

                    if (quote)
                    {
                        decodedName.Append('"');
                    }
                }
            }

            if (addTrailingDelimiter && decodedName.Length > 0)
            {
                decodedName.Append(dnSeparator);
            }

            return decodedName.ToString();
        }

        private static string ReadString(AsnReader tavReader)
        {
            Asn1Tag tag = tavReader.PeekTag();

            if (tag.TagClass != TagClass.Universal)
            {
                throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
            }

            switch ((UniversalTagNumber)tag.TagValue)
            {
                case UniversalTagNumber.BMPString:
                case UniversalTagNumber.IA5String:
                case UniversalTagNumber.PrintableString:
                case UniversalTagNumber.UTF8String:
                case UniversalTagNumber.T61String:
                    return tavReader.GetCharacterString((UniversalTagNumber)tag.TagValue);
                default:
                    throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
            }
        }
    }
}
