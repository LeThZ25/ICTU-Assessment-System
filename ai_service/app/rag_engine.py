import os
import re
import math
from typing import List, Dict, Any
from pypdf import PdfReader

class DocumentStore:
    def __init__(self):
        # Store uploaded docs: doc_id -> { "filename": str, "text": str, "chunks": List[Dict] }
        self.documents: Dict[str, Dict[str, Any]] = {}

    def extract_text(self, filepath: str, filename: str) -> str:
        ext = os.path.splitext(filename)[1].lower()
        text = ""
        try:
            if ext == ".pdf":
                reader = PdfReader(filepath)
                for page in reader.pages:
                    page_text = page.extract_text()
                    if page_text:
                        text += page_text + "\n"
            else:
                # Text / Markdown / Other utf-8 files
                with open(filepath, "r", encoding="utf-8", errors="ignore") as f:
                    text = f.read()
        except Exception as e:
            text = f"Lỗi đọc file: {str(e)}"
        
        # Clean text
        text = re.sub(r'\s+', ' ', text).strip()
        return text

    def chunk_text(self, text: str, chunk_size: int = 500, overlap: int = 100) -> List[str]:
        if not text:
            return []
        
        # First split into sentences if possible
        sentences = re.split(r'(?<=[.?!;:\n])\s+', text)
        chunks = []
        current_chunk = []
        current_len = 0

        for sentence in sentences:
            sentence = sentence.strip()
            if not sentence:
                continue
            if current_len + len(sentence) > chunk_size and current_chunk:
                chunks.append(" ".join(current_chunk))
                # keep some overlap
                current_chunk = [sentence]
                current_len = len(sentence)
            else:
                current_chunk.append(sentence)
                current_len += len(sentence) + 1

        if current_chunk:
            chunks.append(" ".join(current_chunk))

        if not chunks and text:
            chunks = [text[i:i+chunk_size] for i in range(0, len(text), chunk_size - overlap)]

        return chunks

    def add_document(self, doc_id: str, filename: str, filepath: str) -> Dict[str, Any]:
        full_text = self.extract_text(filepath, filename)
        chunks = self.chunk_text(full_text)
        
        chunk_objects = []
        for idx, chunk in enumerate(chunks):
            # simple tokenization for semantic matching
            words = set(re.findall(r'\b\w+\b', chunk.lower()))
            chunk_objects.append({
                "chunk_id": f"{doc_id}_{idx}",
                "text": chunk,
                "words": list(words),
                "index": idx
            })

        doc_data = {
            "doc_id": doc_id,
            "filename": filename,
            "text": full_text,
            "chunks": chunk_objects,
            "num_chunks": len(chunk_objects),
            "word_count": len(full_text.split())
        }
        self.documents[doc_id] = doc_data
        return doc_data

    def search_relevant_chunks(self, query: str, doc_id: str = None, top_k: int = 3) -> List[Dict[str, Any]]:
        query_words = set(re.findall(r'\b\w+\b', query.lower()))
        if not query_words:
            return []

        candidates = []
        target_docs = [self.documents[doc_id]] if (doc_id and doc_id in self.documents) else list(self.documents.values())

        for doc in target_docs:
            for chunk in doc.get("chunks", []):
                chunk_words = set(chunk["words"])
                common = query_words.intersection(chunk_words)
                if common:
                    # Jaccard / Overlap score
                    score = len(common) / (math.sqrt(len(query_words)) * math.sqrt(len(chunk_words)) + 1e-5)
                    candidates.append((score, chunk))

        candidates.sort(key=lambda x: x[0], reverse=True)
        return [item[1] for item in candidates[:top_k]]

# Global singleton
doc_store = DocumentStore()

