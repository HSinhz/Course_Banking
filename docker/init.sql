--
-- PostgreSQL database dump
--

-- Dumped from database version 18.3
-- Dumped by pg_dump version 18.3

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: Accounts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Accounts" (
    "Id" uuid NOT NULL,
    "Name" character varying(100) NOT NULL,
    "Balance" numeric(18,2) NOT NULL,
    "Version" integer NOT NULL
);


--
-- Name: IdempotencyRecords; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."IdempotencyRecords" (
    "Key" character varying(100) NOT NULL,
    "Status" integer NOT NULL,
    "ResponseBody" text,
    "CreatedAt" timestamp with time zone NOT NULL
);


--
-- Name: LedgerEntries; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."LedgerEntries" (
    "Id" uuid NOT NULL,
    "TransferId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "Direction" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "BalanceAfter" numeric(18,2) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL
);


--
-- Name: OutboxMessages; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."OutboxMessages" (
    "Id" uuid NOT NULL,
    "Type" character varying(100) NOT NULL,
    "TransferId" uuid NOT NULL,
    "Status" integer NOT NULL,
    "Attempts" integer NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ProcessedAt" timestamp with time zone,
    "LastError" character varying(500)
);


--
-- Name: Transfers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Transfers" (
    "Id" uuid NOT NULL,
    "IdempotencyKey" character varying(100) NOT NULL,
    "FromAccountId" uuid NOT NULL,
    "ToAccountId" uuid NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Status" integer NOT NULL,
    "Kind" integer NOT NULL,
    "ExternalBankName" character varying(200),
    "CreatedAt" timestamp with time zone NOT NULL
);


--
-- Name: Users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."Users" (
    "Id" uuid NOT NULL,
    "Username" character varying(100) NOT NULL,
    "PasswordHash" text NOT NULL,
    "DisplayName" text NOT NULL
);


--
-- Data for Name: Accounts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Accounts" ("Id", "Name", "Balance", "Version") FROM stdin;
88888888-8888-8888-8888-888888888888	EXTERNAL_OUT	200.99	2
99999999-9999-9999-9999-999999999999	SUSPENSE	0.00	6
33333333-3333-3333-3333-333333333333	Charlie	510.00	1
11111111-1111-1111-1111-111111111111	Alice	783.01	17
22222222-2222-2222-2222-222222222222	Bob	6.00	12
\.


--
-- Data for Name: IdempotencyRecords; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."IdempotencyRecords" ("Key", "Status", "ResponseBody", "CreatedAt") FROM stdin;
ed0df6a2-d7ff-41c5-a072-a51839fb66c3	1	{"TransferId":"a3966d3f-39fe-4c6b-83f1-a12fe527fa5c","FromBalanceAfter":773.01,"Status":"Completed","ServedFromCache":false}	2026-08-03 09:47:58.209723+07
7a826514-2b75-4927-b8d3-bee752b5763b	1	{"TransferId":"a0fbabd0-ab07-48b6-85de-ff2f5aa557f5","FromBalanceAfter":763.01,"Status":"Completed","ServedFromCache":false}	2026-08-03 09:48:17.412663+07
3b28e777-4728-4f95-976e-083435574bea	1	{"TransferId":"959128e1-d681-4d47-a684-2881dae8dd74","FromBalanceAfter":733.01,"Status":"Completed","ServedFromCache":false}	2026-08-03 10:46:39.364448+07
dd6a6145-352c-4b48-98d6-e8bc3c4ca979	1	{"TransferId":"ece3aff3-4184-4327-8754-be0d1ba7b408","FromBalanceAfter":713.01,"Status":"Completed","ServedFromCache":false}	2026-08-03 10:46:39.58306+07
d90432db-8019-4f2a-85cf-8e284bc7541f	1	{"TransferId":"f9e6a9fc-8a0b-40e2-8949-5ab9e6984693","FromBalanceAfter":703.01,"Status":"Completed","ServedFromCache":false}	2026-08-04 11:47:46.850154+07
258aaa95-32b5-4162-8e11-f0ce022c1c2f	1	{"TransferId":"7bffb4a9-fb4b-4bfb-b8c7-caaa5ebda379","FromBalanceAfter":6.00,"Status":"Completed","ServedFromCache":false}	2026-08-04 13:58:38.469783+07
\.


--
-- Data for Name: LedgerEntries; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."LedgerEntries" ("Id", "TransferId", "AccountId", "Direction", "Amount", "BalanceAfter", "CreatedAt") FROM stdin;
2ab9268c-59e5-4a02-852e-bd9541e9ac0b	cbb1b7a2-c027-49a7-af5a-93cdee189986	99999999-9999-9999-9999-999999999999	1	100.00	100.00	2026-08-01 02:52:36.752086+07
7b37df76-3f8a-4013-9528-37c708b159b8	cbb1b7a2-c027-49a7-af5a-93cdee189986	11111111-1111-1111-1111-111111111111	0	100.00	900.00	2026-08-01 02:52:36.744289+07
d8e218e0-f758-491d-b9a4-adfd036ba943	cbb1b7a2-c027-49a7-af5a-93cdee189986	99999999-9999-9999-9999-999999999999	0	100.00	0.00	2026-08-01 02:52:37.089246+07
e46753a0-42ef-4b2c-9ea7-a1f1413b7039	cbb1b7a2-c027-49a7-af5a-93cdee189986	88888888-8888-8888-8888-888888888888	1	100.00	100.00	2026-08-01 02:52:37.089306+07
89aef901-9b8d-4d77-8016-3e9eb8a6f570	e195340a-1282-41ab-afda-ca2ef23967c5	11111111-1111-1111-1111-111111111111	0	100.13	799.87	2026-08-01 02:52:38.907842+07
e0e4d33e-6cfe-496e-9daa-86fec7de3736	e195340a-1282-41ab-afda-ca2ef23967c5	99999999-9999-9999-9999-999999999999	1	100.13	100.13	2026-08-01 02:52:38.907863+07
011f2caf-c90b-4ab7-a9f3-73e444af6da9	e195340a-1282-41ab-afda-ca2ef23967c5	99999999-9999-9999-9999-999999999999	0	100.13	0.00	2026-08-01 02:52:39.129346+07
5c40ede6-712c-4f3e-9efb-f5ac63a8c122	e195340a-1282-41ab-afda-ca2ef23967c5	11111111-1111-1111-1111-111111111111	1	100.13	900.00	2026-08-01 02:52:39.129406+07
6935a4d4-f146-4d3a-9fb1-9fbe3b7d1eb6	53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	99999999-9999-9999-9999-999999999999	1	100.99	100.99	2026-08-01 02:52:40.921447+07
a269a397-e7d8-480c-bc8a-cd558d230ba0	53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	11111111-1111-1111-1111-111111111111	0	100.99	799.01	2026-08-01 02:52:40.921431+07
22c84070-5750-47d8-b36e-bbd3d17c0ceb	53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	99999999-9999-9999-9999-999999999999	0	100.99	0.00	2026-08-01 02:52:42.954698+07
c5067bd6-5528-4d3e-a218-05a70f75a4f3	53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	88888888-8888-8888-8888-888888888888	1	100.99	200.99	2026-08-01 02:52:42.954741+07
51bc38a9-3dc6-48f4-9e3a-5bc551b5346c	fa03abb5-de39-4145-bb7b-db46a5829f4f	22222222-2222-2222-2222-222222222222	1	1.00	1.00	2026-08-03 09:44:25.979735+07
c55443b8-61f3-4b8a-894b-dc5c79174a1d	fa03abb5-de39-4145-bb7b-db46a5829f4f	11111111-1111-1111-1111-111111111111	0	1.00	798.01	2026-08-03 09:44:25.966298+07
a1905c25-c26b-474f-989c-ef1a71422a0a	9deab79a-3bba-47b9-b4ac-d693ff59ab59	22222222-2222-2222-2222-222222222222	1	1.00	2.00	2026-08-03 09:44:43.705857+07
ad609bb0-d984-473c-abcc-5fdfec990427	9deab79a-3bba-47b9-b4ac-d693ff59ab59	11111111-1111-1111-1111-111111111111	0	1.00	797.01	2026-08-03 09:44:43.705738+07
2753a0f5-9737-4016-abf3-39ae60d009e0	309cc07b-1eae-4e04-a5d3-b56ea0d748fb	22222222-2222-2222-2222-222222222222	1	1.00	3.00	2026-08-03 09:44:45.9329+07
44c3574c-7509-4c79-9418-d481bf029e79	309cc07b-1eae-4e04-a5d3-b56ea0d748fb	11111111-1111-1111-1111-111111111111	0	1.00	796.01	2026-08-03 09:44:45.932841+07
3de08068-028b-4f0a-807f-f64b45444acb	f33f7866-d8eb-4a0c-9b7d-076b926880e5	11111111-1111-1111-1111-111111111111	0	1.00	795.01	2026-08-03 09:44:47.995563+07
e41a322e-0fe6-4d25-b341-ca05355acf57	f33f7866-d8eb-4a0c-9b7d-076b926880e5	22222222-2222-2222-2222-222222222222	1	1.00	4.00	2026-08-03 09:44:47.995623+07
206441b6-82ce-4c30-ab71-0332e4ea1a81	7de513b6-b565-4132-9411-8c0729002649	22222222-2222-2222-2222-222222222222	1	1.00	5.00	2026-08-03 09:44:49.271093+07
ca60a87d-3ee1-49c3-b17a-bc7652dc2dc2	7de513b6-b565-4132-9411-8c0729002649	11111111-1111-1111-1111-111111111111	0	1.00	794.01	2026-08-03 09:44:49.271022+07
2bd47550-52e4-4530-ad13-252a0e3c3354	54a786c2-6a02-4bd9-be86-795d390d1f2e	11111111-1111-1111-1111-111111111111	0	1.00	793.01	2026-08-03 09:44:49.41964+07
d93b467b-b2de-4a78-b6c6-46fab3fabf33	54a786c2-6a02-4bd9-be86-795d390d1f2e	22222222-2222-2222-2222-222222222222	1	1.00	6.00	2026-08-03 09:44:49.419671+07
4ca737a2-0f4d-4ca1-818c-0c2de5f02e29	a9f5f71d-abf5-49c2-a8e2-db8fa7534196	22222222-2222-2222-2222-222222222222	1	10.00	16.00	2026-08-03 09:47:21.929712+07
8d324ead-7d35-41d2-a716-a5466f69e6d1	a9f5f71d-abf5-49c2-a8e2-db8fa7534196	11111111-1111-1111-1111-111111111111	0	10.00	783.01	2026-08-03 09:47:21.929691+07
c492c141-2314-45e1-9209-509d30ebf5ef	a3966d3f-39fe-4c6b-83f1-a12fe527fa5c	11111111-1111-1111-1111-111111111111	0	10.00	773.01	2026-08-03 09:47:58.209546+07
f4db3cba-7158-4f1f-891c-06d1802aa4df	a3966d3f-39fe-4c6b-83f1-a12fe527fa5c	22222222-2222-2222-2222-222222222222	1	10.00	26.00	2026-08-03 09:47:58.209694+07
a1316d3d-4cd4-4cd2-861e-b64e01bab97f	a0fbabd0-ab07-48b6-85de-ff2f5aa557f5	22222222-2222-2222-2222-222222222222	1	10.00	36.00	2026-08-03 09:48:17.412634+07
e594ae0b-1d82-4d39-84cf-8a0c9c9a0899	a0fbabd0-ab07-48b6-85de-ff2f5aa557f5	11111111-1111-1111-1111-111111111111	0	10.00	763.01	2026-08-03 09:48:17.412563+07
ab72842d-50dd-45d9-bb2a-6c51f37092fd	959128e1-d681-4d47-a684-2881dae8dd74	22222222-2222-2222-2222-222222222222	1	30.00	66.00	2026-08-03 10:46:39.364435+07
bd5bcee8-9475-4665-a75d-3ebf70fd6e11	959128e1-d681-4d47-a684-2881dae8dd74	11111111-1111-1111-1111-111111111111	0	30.00	733.01	2026-08-03 10:46:39.36429+07
83356fb7-42c8-483a-b533-9d96256b417d	ece3aff3-4184-4327-8754-be0d1ba7b408	11111111-1111-1111-1111-111111111111	0	20.00	713.01	2026-08-03 10:46:39.583038+07
aa669466-f875-41ad-821b-68d26ab3149d	ece3aff3-4184-4327-8754-be0d1ba7b408	22222222-2222-2222-2222-222222222222	1	20.00	86.00	2026-08-03 10:46:39.583054+07
600aea7d-488e-4c82-a968-3883b3aa1954	f9e6a9fc-8a0b-40e2-8949-5ab9e6984693	11111111-1111-1111-1111-111111111111	0	10.00	703.01	2026-08-04 11:47:46.841349+07
a4dd6583-697b-4958-971e-3c63d68e0c1a	f9e6a9fc-8a0b-40e2-8949-5ab9e6984693	33333333-3333-3333-3333-333333333333	1	10.00	510.00	2026-08-04 11:47:46.849561+07
f6d3ff52-62f9-4f98-b8e2-d4ef8080d26a	7bffb4a9-fb4b-4bfb-b8c7-caaa5ebda379	11111111-1111-1111-1111-111111111111	1	80.00	783.01	2026-08-04 13:58:38.469735+07
f7a14c20-1b41-4b9e-9e8d-1445a5a1410a	7bffb4a9-fb4b-4bfb-b8c7-caaa5ebda379	22222222-2222-2222-2222-222222222222	0	80.00	6.00	2026-08-04 13:58:38.469122+07
\.


--
-- Data for Name: OutboxMessages; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."OutboxMessages" ("Id", "Type", "TransferId", "Status", "Attempts", "CreatedAt", "ProcessedAt", "LastError") FROM stdin;
f3dab029-9f7a-40a5-aca0-ae28f68252cd	SettleInterbankTransfer	cbb1b7a2-c027-49a7-af5a-93cdee189986	1	0	2026-08-01 02:52:36.752249+07	2026-08-01 02:52:37.089319+07	\N
f070766f-c362-4ec0-9af6-0f3cbc181f1a	SettleInterbankTransfer	e195340a-1282-41ab-afda-ca2ef23967c5	1	0	2026-08-01 02:52:38.90788+07	2026-08-01 02:52:39.129418+07	\N
706b5f8c-1a2e-4d57-b7fd-43de4b207811	SettleInterbankTransfer	53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	1	2	2026-08-01 02:52:40.921454+07	2026-08-01 02:52:42.957907+07	external bank returned Unknown/timeout
\.


--
-- Data for Name: Transfers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Transfers" ("Id", "IdempotencyKey", "FromAccountId", "ToAccountId", "Amount", "Status", "Kind", "ExternalBankName", "CreatedAt") FROM stdin;
cbb1b7a2-c027-49a7-af5a-93cdee189986	ib-ok-2bc38c92-2eef-4655-bb50-e1f562808fdf	11111111-1111-1111-1111-111111111111	99999999-9999-9999-9999-999999999999	100.00	1	1	Techcombank	2026-08-01 02:52:36.73255+07
e195340a-1282-41ab-afda-ca2ef23967c5	ib-fail-c1db5b88-c755-48c8-807b-5676b602bc8a	11111111-1111-1111-1111-111111111111	99999999-9999-9999-9999-999999999999	100.13	3	1	Techcombank	2026-08-01 02:52:38.907773+07
53e56f76-a7a1-4311-8c4d-7fa977e6a0e8	ib-timeout-417d8985-7d31-4003-9385-0b883afd6fd1	11111111-1111-1111-1111-111111111111	99999999-9999-9999-9999-999999999999	100.99	1	1	Techcombank	2026-08-01 02:52:40.921343+07
fa03abb5-de39-4145-bb7b-db46a5829f4f	20f2f7f8-346a-461a-ad70-dbfe97a5818f	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:25.930587+07
9deab79a-3bba-47b9-b4ac-d693ff59ab59	104b8187-802f-4fb3-9cc8-f5995f299524	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:43.705373+07
309cc07b-1eae-4e04-a5d3-b56ea0d748fb	a862fe28-c58e-45df-a0b5-cd12637eafe3	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:45.932747+07
f33f7866-d8eb-4a0c-9b7d-076b926880e5	40fe0037-2397-451f-a9c1-829d1764ad45	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:47.995444+07
7de513b6-b565-4132-9411-8c0729002649	98b78010-e0df-4c0a-ae1f-4b264ea8d818	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:49.270905+07
54a786c2-6a02-4bd9-be86-795d390d1f2e	cc6d4a1c-0cb0-4c3f-952a-bf6c74708916	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	1.00	1	0	\N	2026-08-03 09:44:49.419587+07
a9f5f71d-abf5-49c2-a8e2-db8fa7534196	ed0df6a2-d7ff-41c5-a072-a51839fb66c3	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	10.00	1	0	\N	2026-08-03 09:47:21.929611+07
a3966d3f-39fe-4c6b-83f1-a12fe527fa5c	ed0df6a2-d7ff-41c5-a072-a51839fb66c3	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	10.00	1	0	\N	2026-08-03 09:47:58.209442+07
a0fbabd0-ab07-48b6-85de-ff2f5aa557f5	7a826514-2b75-4927-b8d3-bee752b5763b	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	10.00	1	0	\N	2026-08-03 09:48:17.412472+07
959128e1-d681-4d47-a684-2881dae8dd74	3b28e777-4728-4f95-976e-083435574bea	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	30.00	1	0	\N	2026-08-03 10:46:39.361202+07
ece3aff3-4184-4327-8754-be0d1ba7b408	dd6a6145-352c-4b48-98d6-e8bc3c4ca979	11111111-1111-1111-1111-111111111111	22222222-2222-2222-2222-222222222222	20.00	1	0	\N	2026-08-03 10:46:39.582971+07
f9e6a9fc-8a0b-40e2-8949-5ab9e6984693	d90432db-8019-4f2a-85cf-8e284bc7541f	11111111-1111-1111-1111-111111111111	33333333-3333-3333-3333-333333333333	10.00	1	0	\N	2026-08-04 11:47:46.817387+07
7bffb4a9-fb4b-4bfb-b8c7-caaa5ebda379	258aaa95-32b5-4162-8e11-f0ce022c1c2f	22222222-2222-2222-2222-222222222222	11111111-1111-1111-1111-111111111111	80.00	1	0	\N	2026-08-04 13:58:38.461735+07
\.


--
-- Data for Name: Users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public."Users" ("Id", "Username", "PasswordHash", "DisplayName") FROM stdin;
32ce643b-54c4-49aa-9aa8-102fd4a43d27	demo	G2DDpXECSd4PFtAe1byrwQ==.ZHPvoK3a08Uc/cqmSj8iyc8A5RBXFKeU7o8Hmo6Xnjg=	Demo User
\.


--
-- Name: Accounts PK_Accounts; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Accounts"
    ADD CONSTRAINT "PK_Accounts" PRIMARY KEY ("Id");


--
-- Name: IdempotencyRecords PK_IdempotencyRecords; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."IdempotencyRecords"
    ADD CONSTRAINT "PK_IdempotencyRecords" PRIMARY KEY ("Key");


--
-- Name: LedgerEntries PK_LedgerEntries; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."LedgerEntries"
    ADD CONSTRAINT "PK_LedgerEntries" PRIMARY KEY ("Id");


--
-- Name: OutboxMessages PK_OutboxMessages; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."OutboxMessages"
    ADD CONSTRAINT "PK_OutboxMessages" PRIMARY KEY ("Id");


--
-- Name: Transfers PK_Transfers; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Transfers"
    ADD CONSTRAINT "PK_Transfers" PRIMARY KEY ("Id");


--
-- Name: Users PK_Users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."Users"
    ADD CONSTRAINT "PK_Users" PRIMARY KEY ("Id");


--
-- Name: IX_LedgerEntries_AccountId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LedgerEntries_AccountId" ON public."LedgerEntries" USING btree ("AccountId");


--
-- Name: IX_LedgerEntries_TransferId; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_LedgerEntries_TransferId" ON public."LedgerEntries" USING btree ("TransferId");


--
-- Name: IX_OutboxMessages_Status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX "IX_OutboxMessages_Status" ON public."OutboxMessages" USING btree ("Status");


--
-- Name: IX_Users_Username; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX "IX_Users_Username" ON public."Users" USING btree ("Username");
