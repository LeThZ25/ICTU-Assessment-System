# -*- coding: utf-8 -*-
"""
Playwright script to interact live with the new 3-in-1 Studio
and capture high-resolution screenshots for demonstration.
"""

import os
import sys
import time
from playwright.sync_api import sync_playwright

ARTIFACT_DIR = r"C:\Users\Admin\.gemini\antigravity\brain\fa163122-b38c-4315-b08a-2a6606947f0f"
BASE_URL = "http://localhost:5000"

def run_demonstration():
    print(f"[*] Starting Playwright Microsoft Edge automation at {BASE_URL}...", flush=True)
    with sync_playwright() as p:
        browser = p.chromium.launch(channel="msedge", headless=True)
        context = browser.new_context(viewport={"width": 1400, "height": 900})
        page = context.new_page()

        # Step 1: Open Lecturer Portal
        print("[1] Navigating to Lecturer Portal...", flush=True)
        page.goto(BASE_URL, wait_until="domcontentloaded")
        time.sleep(1)

        # Step 1.5: Login via preset
        print("[1.5] Logging in as TS. Nguyễn Văn A...", flush=True)
        login_btn = page.locator("#loginGateOverlay button[onclick*='loginAsLecturer']").first
        if login_btn.is_visible():
            login_btn.click()
            time.sleep(1)

        # Step 2: Click on Studio Soan De Thi (3-in-1 tab)
        print("[2] Switching to Unified 3-in-1 Studio Tab...", flush=True)
        page.click("button[onclick*=\"switchTab('exam-creator')\"]")
        time.sleep(1)

        # Screenshot 1: Unified 3-in-1 Studio Canvas
        shot1_path = os.path.join(ARTIFACT_DIR, "demo_studio_01_unified_overview.png")
        page.screenshot(path=shot1_path, full_page=False)
        print(f" -> Saved: {shot1_path}", flush=True)

        # Step 3: Open Custom Flexible Question Modal
        print("[3] Opening Custom Question Modal...", flush=True)
        page.click("button:has-text('Soạn Câu Hỏi Tùy Biến')")
        time.sleep(0.8)

        # Step 4: Interact with Modal - Change to Multi-Select MCQ and add 5th option
        print("[4] Interacting with Dynamic Options...", flush=True)
        page.select_option("#cqType", "mcq_multi")
        time.sleep(0.5)

        # Add 5th option (E)
        page.click("button:has-text('Thêm Phương Án Lựa Chọn')")
        time.sleep(0.5)

        # Fill question content
        page.fill("#cqQuestionText", "Trong kiến trúc hệ thống phân tán chịu lỗi cao (High Availability), những kỹ thuật nào sau đây được áp dụng để đảm bảo tính sẵn sàng của dữ liệu? (Chọn tất cả các đáp án đúng)")
        
        # Fill options
        inputs = page.locator("#cqOptionsList input[type='text']").all()
        labels = [
            "Replication (Sao chép dữ liệu đa vùng với giao thức 2-Phase Commit)",
            "Master-Slave Redundancy với cơ chế Automatic Failover",
            "Chỉ lưu trữ trên một Server cục bộ duy nhất không sao lưu",
            "Sử dụng Load Balancer để điều phối tải và loại trừ node gặp sự cố",
            "Phân vùng dữ liệu (Data Sharding) để tránh Single Point of Failure"
        ]
        for i, val in enumerate(labels):
            if i < len(inputs):
                inputs[i].fill(val)

        # Check checkboxes for correct options (A, B, D, E)
        checks = page.locator("#cqOptionsList input[type='checkbox']").all()
        if len(checks) >= 5:
            checks[0].check()
            checks[1].check()
            checks[3].check()
            checks[4].check()

        # Screenshot 2: Custom Modal with 5 options and multi-select
        shot2_path = os.path.join(ARTIFACT_DIR, "demo_studio_02_dynamic_mcq_5choices.png")
        page.screenshot(path=shot2_path, full_page=False)
        print(f" -> Saved: {shot2_path}", flush=True)

        # Step 5: Save Custom Question into Exam Canvas
        print("[5] Saving Question to Live Exam Canvas...", flush=True)
        page.click("button:has-text('Thêm Vào Đề Thi Này')")
        time.sleep(1)

        # Step 6: Expand the AI CLO Matrix Generator Drawer
        print("[6] Expanding AI Matrix Drawer...", flush=True)
        page.click("button:has-text('Sinh Bằng AI (Ma Trận CLO)')")
        time.sleep(0.8)

        # Screenshot 3: Live Exam Canvas with the question and real-time stats
        shot3_path = os.path.join(ARTIFACT_DIR, "demo_studio_03_live_canvas_with_stats.png")
        page.screenshot(path=shot3_path, full_page=False)
        print(f" -> Saved: {shot3_path}", flush=True)

        # Step 7: Open modal again to show Code Challenge Builder
        print("[7] Demonstrating Code Challenge Builder...", flush=True)
        page.click("button:has-text('Soạn Câu Hỏi Tùy Biến')")
        time.sleep(0.5)
        page.select_option("#cqType", "code")
        time.sleep(0.5)
        page.fill("#cqQuestionText", "Viết câu truy vấn SQL tìm Top 3 sinh viên có điểm trung bình tích lũy (GPA) cao nhất thuộc khoa Công nghệ thông tin.")
        
        # Add a testcase
        add_tc_btn = page.locator("button:has-text('Thêm Test Case')")
        if add_tc_btn.is_visible():
            add_tc_btn.click()
            time.sleep(0.3)

        shot4_path = os.path.join(ARTIFACT_DIR, "demo_studio_04_code_challenge_builder.png")
        page.screenshot(path=shot4_path, full_page=False)
        print(f" -> Saved: {shot4_path}", flush=True)

        # Step 8: Switch to Rubric Builder
        print("[8] Demonstrating Essay Rubric Builder...", flush=True)
        page.select_option("#cqType", "essay")
        time.sleep(0.5)
        page.fill("#cqQuestionText", "Anh/chị hãy phân tích kiến trúc Microservices so với Monolith trong các hệ thống doanh nghiệp lớn. Nêu rõ ưu nhược điểm và giải pháp đồng bộ dữ liệu giữa các dịch vụ.")
        
        add_rubric_btn = page.locator("button:has-text('Thêm Tiêu Chí')")
        if add_rubric_btn.is_visible():
            add_rubric_btn.click()
            time.sleep(0.3)

        shot5_path = os.path.join(ARTIFACT_DIR, "demo_studio_05_essay_rubric_builder.png")
        page.screenshot(path=shot5_path, full_page=False)
        print(f" -> Saved: {shot5_path}", flush=True)

        browser.close()
        print("[+] Playwright demonstration completed successfully!", flush=True)

if __name__ == "__main__":
    run_demonstration()
